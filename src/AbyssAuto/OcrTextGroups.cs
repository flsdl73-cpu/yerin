using System.Drawing;
namespace AbyssAuto;
public sealed record TextHit(string Text,Rectangle Bounds,string Source="raw");
public static class OcrTextGroups
{
    // Call per OCR line: never join text from different lines/cards/buttons globally.
    public static IReadOnlyList<TextHit> ExactPhrases(IReadOnlyList<TextHit> words,IReadOnlyCollection<string> phrases)
    {
        var expected=phrases.Select(TextRules.Normalize).ToHashSet();var result=new List<TextHit>();
        for(int i=0;i<words.Count;i++)
        {
            var text="";var bounds=words[i].Bounds;
            for(int count=1;count<=4&&i+count<=words.Count;count++)
            {
                var word=words[i+count-1];
                if(count>1&&word.Bounds.Left-bounds.Right>Math.Max(24,Math.Max(word.Bounds.Height,bounds.Height)*2))break;
                text+=(count>1?" ":"")+word.Text;bounds=Rectangle.Union(bounds,word.Bounds);
                if(expected.Contains(TextRules.Normalize(text)))result.Add(new(text,bounds,word.Source+"-phrase"));
            }
        }
        return result;
    }
}
