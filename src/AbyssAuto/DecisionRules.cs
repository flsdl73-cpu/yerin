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
