namespace AbyssAuto;

// No desktop dependencies: replayable recognition decisions for regression tests.
public sealed class TransitionGate(bool requireSourceAbsent, int maximumRetries)
{
    private int consecutiveNext;
    public int Attempts { get; private set; }
    public bool Observe(bool source, bool next)
    {
        consecutiveNext=next && (!requireSourceAbsent||!source) ? consecutiveNext+1 : 0;
        return consecutiveNext>=2;
    }
    public void BeforeInput()
    {
        if(Attempts>=maximumRetries+1)throw new InvalidOperationException("최대 재시도 초과");
        Attempts++;
        consecutiveNext=0;
    }
}
public static class TextRules
{
    public static string Normalize(string value) => string.Concat(value.Where(c => !char.IsWhiteSpace(c)));
    public static bool Matches(string actual, string expected, bool fuzzy=false)
    {
        actual=Normalize(actual); expected=Normalize(expected);
        if(expected.Length==0)return false;
        if(actual.Contains(expected,StringComparison.Ordinal))return true;
        if(!fuzzy||expected.Length<3||Math.Abs(actual.Length-expected.Length)>1)return false;
        var previous=Enumerable.Range(0,expected.Length+1).ToArray();
        for(var i=1;i<=actual.Length;i++)
        {
            var row=new int[expected.Length+1];row[0]=i;
            for(var j=1;j<=expected.Length;j++)row[j]=Math.Min(Math.Min(row[j-1]+1,previous[j]+1),previous[j-1]+(actual[i-1]==expected[j-1]?0:1));
            previous=row;
        }
        return previous[^1]<=1;
    }
}

public static class WhiteTextRules
{
    public static bool IsGlyph(byte r,byte g,byte b) => Math.Min(r,Math.Min(g,b))>=190 && Math.Max(r,Math.Max(g,b))-Math.Min(r,Math.Min(g,b))<=60;
}

public enum Stage { Menu, Abyss, Destination, Enter, Battle, Result, Retry }

public static class RecognitionRules
{
    public static bool HasTextEvidence(IEnumerable<string> actual, string[] primary, string[] required, bool fuzzy=false)
    {
        var lines=actual.ToArray();
        return primary.Any(phrase=>lines.Any(line=>TextRules.Matches(line,phrase,fuzzy)))
            && required.All(phrase=>lines.Any(line=>TextRules.Matches(line,phrase)));
    }
}

public static class StartStagePolicy
{
    public static Stage Resolve(bool resume,IReadOnlyCollection<Stage> recognized,bool mainConfirmed)
    {
        if(recognized.Count>1)throw new InvalidOperationException("현재 단계 판정이 충돌합니다. 입력 없이 정지합니다.");
        if(recognized.Count==1)
        {
            var stage=recognized.Single();
            if(resume||stage==Stage.Abyss)return stage;
            var name=stage switch { Stage.Battle=>"전투",Stage.Result=>"결과",Stage.Retry=>"보상",Stage.Enter=>"입장 상세",Stage.Destination=>"목적지 선택",_=>"메뉴" };
            throw new InvalidOperationException($"현재 {name} 화면입니다. '현재 화면 재판별 후 시작'을 켜고 시작하세요.");
        }
        if(!resume&&mainConfirmed)return Stage.Menu;
        throw new InvalidOperationException("현재 단계가 불명확합니다. 입력 없이 정지합니다.");
    }
}
