using System.Text.RegularExpressions;
namespace AbyssAuto;

public enum RevivalAction { Wait,Click,Recovered,Stop }
public sealed record RevivalDecision(RevivalAction Action,string Reason);
public sealed class RevivalRules
{
    private readonly int maximumAttempts;private readonly double interval;
    private double lastSent=double.NegativeInfinity;private long lastFrame=-1,pendingFrame=-1;private int normalFrames;
    public int Attempts {get;private set;}
    public RevivalRules(int maximumAttempts=4,double interval=4)
    {
        if(maximumAttempts is <1 or >4||!double.IsFinite(interval)||interval<4)throw new ArgumentOutOfRangeException(nameof(interval));
        this.maximumAttempts=maximumAttempts;this.interval=interval;
    }
    public static int? Remaining(string ocr)
    {
        var text=TextRules.Normalize(ocr);var match=Regex.Match(text,@"남은부활횟수(\d{1,2})/(\d{1,2})(?!\d)");
        if(!match.Success)return null;var remaining=int.Parse(match.Groups[1].Value);var total=int.Parse(match.Groups[2].Value);
        return total>0&&remaining<=total?remaining:null;
    }
    public RevivalDecision Observe(bool dead,bool button,bool normal,bool purchase,int? remaining,bool fresh,long frame,double now)
    {
        if(purchase)return new(RevivalAction.Stop,"구매/재료 부족 안내: 자동 구매하지 않습니다.");
        if(remaining==0)return new(RevivalAction.Stop,"남은 부활 횟수가 0입니다.");
        if(!fresh||frame<=lastFrame)return new(RevivalAction.Wait,"새 화면 확인 대기");
        lastFrame=frame;
        if(!dead&&!button&&normal)
        {
            if(++normalFrames>=2)return new(RevivalAction.Recovered,"부활 화면 소멸과 정상 화면 2회 확인");
            return new(RevivalAction.Wait,"정상 화면 추가 확인");
        }
        normalFrames=0;
        if(!dead||!button)return new(RevivalAction.Wait,"행동불능과 정확한 부활 버튼을 함께 확인하는 중");
        if(now-lastSent<interval)return new(RevivalAction.Wait,"부활 입력 최소 간격 대기");
        if(Attempts>=maximumAttempts)return new(RevivalAction.Stop,"부활 재시도 한도 초과");
        pendingFrame=frame;return new(RevivalAction.Click,"행동불능과 여기서 부활 버튼 확인");
    }
    public void Sent(long frame,double now)
    {
        if(frame!=pendingFrame||Attempts>=maximumAttempts)throw new InvalidOperationException("검증되지 않은 부활 입력입니다.");
        Attempts++;lastSent=now;pendingFrame=-1;
    }
}

public static class RevivalColor
{
    public static bool IsOrange(double r,double g,double b)=>r>=160&&g>=75&&b<=180&&r-g>=25&&g-b>=30;
}
