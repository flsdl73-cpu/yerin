using System.Diagnostics;
namespace AbyssAuto;
public sealed class Engine(Configuration config, GameWindow window, Vision vision, Journal journal)
{
    public event Action<Stage>? Changed;
    public event Action<string>? Notice;
    public event Action<int,TimeSpan>? Completed;
    public Stage Stage { get; private set; }
    public int Count { get; private set; }
    private Bitmap? lastFrame;
    private readonly Dictionary<string,Detection> evidence=[];
    private IInput? input;
    private Stopwatch run=new();
    private string destination="husang";
    private bool skip;
    private PlayMode playMode;
    private bool autoRevive;
    private long frameId,capturedAt;
    private long? lastRevivalSent;
    private async Task<Detection> Detect(Bitmap frame,string name,CancellationToken token)
    {
        var result=await vision.Detect(frame,name,token); evidence[name]=result; return result;
    }
    private Bitmap Capture()
    {
        var next=window.Capture(); lastFrame?.Dispose(); lastFrame=next; evidence.Clear();frameId++;capturedAt=Stopwatch.GetTimestamp(); return lastFrame;
    }
    private void Set(Stage value) { Stage=value; journal.Write($"단계: {value}"); Changed?.Invoke(value); }
    public async Task Run(string destination, bool skip, int limit, bool resume, PlayMode playMode, bool autoRevive, CancellationToken token)
    {
        this.destination=destination; this.skip=skip; this.playMode=playMode; this.autoRevive=autoRevive; Count=0;run=Stopwatch.StartNew();
        try
        {
            if(!config.Targets[destination].Images.Any(f=>File.Exists(Path.Combine(config.Root,"images",f))))
                throw new InvalidOperationException("선택한 목적지의 실제 배너 사진을 먼저 등록하세요.");
            using var device=InputFactory.Create(window);input=device;
            journal.Write($"입력 방식: {InputFactory.Description}");
            journal.Write("3초 후 시작합니다. 게임 창을 활성화하세요. F10: 즉시 정지");
            for(int i=3;i>0;i--) {System.Media.SystemSounds.Asterisk.Play();await Task.Delay(1000,token);}
            Set(await Classify(resume,token));
            while(true)
            {
                token.ThrowIfCancellationRequested();
                switch(Stage)
                {
                    case Stage.Menu: await Menu(token);Set(Stage.Abyss);break;
                    case Stage.Abyss: await Transition("abyss",destination,false,config.Scenario.ButtonTimeoutSeconds,token);Set(Stage.Destination);break;
                    case Stage.Destination: await Transition(destination,"enter",false,config.Scenario.ButtonTimeoutSeconds,token);Set(Stage.Enter);break;
                    case Stage.Enter: await Transition("enter","battle",true,config.Scenario.ButtonTimeoutSeconds,token);Set(Stage.Battle);break;
                    case Stage.Battle: await Battle(token);Set(Stage.Result);break;
                    case Stage.Result:
                        await Transition("result",RepeatRoute.RewardTarget(playMode),false,config.Scenario.RetryTimeoutSeconds,token);
                        Count++; Completed?.Invoke(Count,run.Elapsed);journal.Write($"{Count}판 완료");
                        if(limit>0 && Count>=limit) {System.Media.SystemSounds.Asterisk.Play();journal.Write("목표 횟수 완료. 보상 화면에서 정지합니다.");return;}
                        Set(RepeatRoute.RewardStage(playMode));break;
                    case Stage.Retry: await Transition("retry","battle",false,config.Scenario.RetryTimeoutSeconds,token);Set(Stage.Battle);break;
                    case Stage.OtherDungeon: await Transition("other_dungeon",destination,false,config.Scenario.ButtonTimeoutSeconds,token);Set(Stage.Destination);break;
                }
            }
        }
        catch(OperationCanceledException) { journal.Write("사용자 정지"); }
        catch(Exception error)
        {
            journal.Failure(lastFrame,Stage,error,evidence);
            for(int i=0;i<6;i++) { if(token.IsCancellationRequested) break;System.Media.SystemSounds.Hand.Play(); await Task.Delay(500); }
        }
        finally {input=null;lastFrame?.Dispose();lastFrame=null;run.Stop();}
    }
    private async Task<Stage> Classify(bool resume,CancellationToken token)
    {
        var frame=await PrepareFrame(token); var candidates=new List<Stage>();
        foreach(var (name,stage) in new (string,Stage)[]{("result",Stage.Result),(RepeatRoute.RewardTarget(playMode),RepeatRoute.RewardStage(playMode)),("enter",Stage.Enter),(destination,Stage.Destination),("menu",Stage.Abyss),("battle",Stage.Battle)})
            if((await Detect(frame,name,token)).Passed) candidates.Add(stage);
        var mainConfirmed=candidates.Count==0 && (await Detect(frame,"chat",token)).Passed;
        var selected=StartStagePolicy.Resolve(resume,candidates,mainConfirmed);
        journal.Write("현재 화면 재분류",selected.ToString());return selected;
    }
    private async Task Menu(CancellationToken token)
    {
        var timer=Stopwatch.StartNew();bool sent=false;
        while(timer.Elapsed.TotalSeconds<15)
        {
            var frame=await PrepareFrame(token);
            if((await Detect(frame,"menu",token)).Passed) return;
            if(!sent && (await Detect(frame,"chat",token)).Passed)
            {
                token.ThrowIfCancellationRequested();journal.Input("ESC 시도");await input!.Key(0x01,token);journal.Write("ESC 입력 전달 성공");sent=true;await Task.Delay(700,token);
            }
            else await Task.Delay(450,token);
        }
        throw new TimeoutException("메뉴 열림 확인 실패. ESC를 추가로 누르지 않습니다.");
    }
    private async Task GuardPurchase(Bitmap frame,CancellationToken token)
    {
        var purchase=await Detect(frame,"purchase",token);
        if(purchase.Passed) throw new InvalidOperationException($"구매/재료 부족 가능 화면: {purchase.Ocr}");
    }
    private async Task<Bitmap> PrepareFrame(CancellationToken token)
    {
        var timer=Stopwatch.StartNew();
        while(true)
        {
            var frame=Capture();await GuardPurchase(frame,token);
            if(!autoRevive)return frame;
            var dead=await Detect(frame,"incapacitated",token);
            var button=await Detect(frame,"revive",token);
            if(!dead.Passed&&!button.Passed)return frame;
            if(dead.Passed&&button.Passed)
            {
                await Revive(token);timer.Restart();continue;
            }
            if(timer.Elapsed.TotalSeconds>config.Scenario.ButtonTimeoutSeconds)
                throw new TimeoutException("행동불능과 여기서 부활 버튼을 함께 확인하지 못했습니다. 입력 없이 정지합니다.");
            await Task.Delay(450,token);
        }
    }
    private async Task Revive(CancellationToken token)
    {
        var timer=Stopwatch.StartNew();var policy=new RevivalRules(config.Scenario.MaximumRetries+1,config.Scenario.RevivalIntervalSeconds);
        journal.Write("행동불능 확인: 여기서 부활 처리 시작");Notice?.Invoke("행동불능 · 여기서 부활 확인 중");
        while(timer.Elapsed.TotalSeconds<config.Scenario.RevivalTimeoutSeconds)
        {
            var frame=Capture();await GuardPurchase(frame,token);
            var dead=await Detect(frame,"incapacitated",token);var button=await Detect(frame,"revive",token);
            int? remaining=null;bool normal=false;
            if(dead.Passed&&button.Passed)
            {
                var counter=await Detect(frame,"revive_count",token);remaining=RevivalRules.Remaining(counter.Ocr);
                if((await Detect(frame,"result",token)).Passed)throw new InvalidOperationException("결과 화면과 행동불능 화면 판정이 충돌합니다.");
            }
            else if(!dead.Passed&&!button.Passed)
                normal=(await Detect(frame,"battle",token)).Passed||(await Detect(frame,"result",token)).Passed||(await Detect(frame,RepeatRoute.RewardTarget(playMode),token)).Passed;
            var fresh=Stopwatch.GetElapsedTime(capturedAt).TotalSeconds<=1.5;
            var decision=policy.Observe(dead.Passed,button.Passed,normal,false,remaining,fresh,frameId,timer.Elapsed.TotalSeconds);
            if(decision.Action==RevivalAction.Stop)throw new InvalidOperationException(decision.Reason);
            if(decision.Action==RevivalAction.Recovered)
            {
                journal.Write("부활 화면 종료와 정상 화면 전환 확인",new {attempts=policy.Attempts,remaining});Changed?.Invoke(Stage);return;
            }
            if(decision.Action==RevivalAction.Click)
            {
                if(lastRevivalSent is {} sent && Stopwatch.GetElapsedTime(sent).TotalSeconds<config.Scenario.RevivalIntervalSeconds) {await Task.Delay(450,token);continue;}
                token.ThrowIfCancellationRequested();journal.Write("부활 입력 근거",new {dead,button,remaining});
                journal.Input($"여기서 부활 클릭 시도 {button.Bounds}");await input!.Click(button.Bounds,token);
                lastRevivalSent=Stopwatch.GetTimestamp();policy.Sent(frameId,timer.Elapsed.TotalSeconds);journal.Write($"부활 클릭 전달 성공 · 시도 {policy.Attempts}");
            }
            await Task.Delay(450,token);
        }
        throw new TimeoutException("부활 후 정상 화면 전환을 확인하지 못했습니다.");
    }
    private async Task Transition(string source,string next,bool space,int seconds,CancellationToken token)
    {
        var timer=Stopwatch.StartNew();var gate=new TransitionGate(next=="battle",config.Scenario.MaximumRetries);var lastAction=TimeSpan.MinValue;
        while(timer.Elapsed.TotalSeconds<seconds)
        {
            var frame=await PrepareFrame(token);
            var current=await Detect(frame,source,token);
            var following=await Detect(frame,next,token);
            // Explicit next screen on two fresh frames. For transition to battle, the previous button must disappear too.
            if(gate.Observe(current.Passed,following.Passed)) {journal.Write($"전환 확인: {source} → {next}",following);return;}
            if(following.Passed) { await Task.Delay(450,token);continue; }
            if(current.Passed && (gate.Attempts==0 || timer.Elapsed-lastAction>TimeSpan.FromMilliseconds(650)))
            {
                journal.Write("입력 후보 근거",current);
                if(space)
                {
                    await Task.Delay(1000,token);
                    frame=await PrepareFrame(token);
                    current=await Detect(frame,source,token);
                    if(!current.Passed) continue;
                    gate.BeforeInput();journal.Input("SPACE 시도");await input!.Key(0x39,token);journal.Write("SPACE 입력 전달 성공");
                }
                else { gate.BeforeInput();journal.Input($"{source} 클릭 시도 {current.Bounds}");await input!.Click(current.Bounds,token);journal.Write("클릭 입력 전달 성공"); }
                journal.Write("입력 근거",current);lastAction=timer.Elapsed;
                await Task.Delay(Random.Shared.Next(400,651),token);
            }
            else await Task.Delay(Random.Shared.Next(380,521),token);
        }
        throw new TimeoutException($"{source} → {next}: 다음 화면 확인 시간 초과");
    }
    private async Task Battle(CancellationToken token)
    {
        var timer=Stopwatch.StartNew();var unknown=new Stopwatch();
        while(timer.Elapsed.TotalSeconds<config.Scenario.BattleTimeoutSeconds)
        {
            var frame=await PrepareFrame(token);
            if(skip)
            {
                var button=await Detect(frame,"skip",token);
                if(button.Passed)
                {
                    journal.Input($"넘기기 클릭 시도 {button.Bounds}");await input!.Click(button.Bounds,token);journal.Write("넘기기 입력 전달 성공",button);
                    await Task.Delay(300,token);continue;
                }
            }
            if((await Detect(frame,"result",token)).Passed) return;
            if((await Detect(frame,"battle",token)).Passed) unknown.Reset();
            else
            {
                if(!unknown.IsRunning) unknown.Start();
                if(unknown.Elapsed.TotalSeconds>config.Scenario.UnknownTimeoutSeconds) throw new InvalidOperationException("전투 근거가 없는 화면이 지속됩니다. 부활·재접속 등 미등록 화면을 확인하세요.");
            }
            await Task.Delay(Random.Shared.Next(620,801),token);
        }
        throw new TimeoutException("전투 결과 대기 시간 초과");
    }
}
