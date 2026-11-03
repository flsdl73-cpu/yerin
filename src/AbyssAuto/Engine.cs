using System.Diagnostics;
namespace AbyssAuto;
public sealed class Engine(Configuration config, GameWindow window, Vision vision, Journal journal)
{
    public event Action<Stage>? Changed;
    public event Action<int,TimeSpan>? Completed;
    public Stage Stage { get; private set; }
    public int Count { get; private set; }
    private Bitmap? lastFrame;
    private readonly Dictionary<string,Detection> evidence=[];
    private IInput? input;
    private Stopwatch run=new();
    private string destination="husang";
    private bool skip;
    private async Task<Detection> Detect(Bitmap frame,string name,CancellationToken token)
    {
        var result=await vision.Detect(frame,name,token); evidence[name]=result; return result;
    }
    private Bitmap Capture()
    {
        var next=window.Capture(); lastFrame?.Dispose(); lastFrame=next; evidence.Clear(); return lastFrame;
    }
    private void Set(Stage value) { Stage=value; journal.Write($"단계: {value}"); Changed?.Invoke(value); }
    public async Task Run(string destination, bool skip, int limit, bool resume, CancellationToken token)
    {
        this.destination=destination; this.skip=skip; Count=0;run=Stopwatch.StartNew();
        try
        {
            if(!config.Targets[destination].Images.Any(f=>File.Exists(Path.Combine(config.Root,"images",f))))
                throw new InvalidOperationException("선택한 목적지의 실제 배너 사진을 먼저 등록하세요.");
            using var driver=new InterceptionInput(window);input=driver;
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
                        await Transition("result","retry",false,config.Scenario.RetryTimeoutSeconds,token);
                        Count++; Completed?.Invoke(Count,run.Elapsed);journal.Write($"{Count}판 완료");
                        if(limit>0 && Count>=limit) {System.Media.SystemSounds.Asterisk.Play();journal.Write("목표 횟수 완료. 보상 화면에서 정지합니다.");return;}
                        Set(Stage.Retry);break;
                    case Stage.Retry: await Transition("retry","battle",false,config.Scenario.RetryTimeoutSeconds,token);Set(Stage.Battle);break;
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
        var frame=Capture(); await Guard(frame,token); var candidates=new List<Stage>();
        foreach(var (name,stage) in new (string,Stage)[]{("result",Stage.Result),("retry",Stage.Retry),("enter",Stage.Enter),(destination,Stage.Destination),("menu",Stage.Abyss),("battle",Stage.Battle)})
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
            var frame=Capture();await Guard(frame,token);
            if((await Detect(frame,"menu",token)).Passed) return;
            if(!sent && (await Detect(frame,"chat",token)).Passed)
            {
                token.ThrowIfCancellationRequested();journal.Input("ESC 시도");await input!.Key(0x01,token);journal.Write("ESC 입력 전달 성공");sent=true;await Task.Delay(700,token);
            }
            else await Task.Delay(450,token);
        }
        throw new TimeoutException("메뉴 열림 확인 실패. ESC를 추가로 누르지 않습니다.");
    }
    private async Task Guard(Bitmap frame,CancellationToken token)
    {
        var purchase=await Detect(frame,"purchase",token);
        if(purchase.Passed) throw new InvalidOperationException($"구매/재료 부족 가능 화면: {purchase.Ocr}");
    }
    private async Task Transition(string source,string next,bool space,int seconds,CancellationToken token)
    {
        var timer=Stopwatch.StartNew();var gate=new TransitionGate(next=="battle",config.Scenario.MaximumRetries);var lastAction=TimeSpan.MinValue;
        while(timer.Elapsed.TotalSeconds<seconds)
        {
            var frame=Capture();await Guard(frame,token);
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
                    frame=Capture();await Guard(frame,token);
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
            var frame=Capture();await Guard(frame,token);
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
