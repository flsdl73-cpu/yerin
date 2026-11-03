using System.Text.Json;
namespace AbyssAuto;

// Uses the exact production Vision path. No GameWindow or input driver is created.
public static class OfflineValidator
{
    private static readonly Dictionary<string,string[]> Expected=new()
    {
        ["chat"]=["main","battle","battle-live-01","battle-live-02","battle-dither"], ["menu"]=["menu"], ["abyss"]=["menu"],
        ["husang"]=["destinations"], ["kwanggi"]=["destinations"], ["moolgil"]=["destinations"],
        ["enter"]=["enter"], ["result"]=["result"], ["retry"]=["reward"], ["battle"]=["battle","battle-live-01","battle-live-02","battle-dither"],
        ["skip"]=[], ["purchase"]=[], ["other_dungeon"]=["reward"]
    };
    public static async Task<int> Run(string screenDirectory,string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        foreach(var file in Directory.GetFiles(outputDirectory,"*-regions.png").Concat(new[]{Path.Combine(outputDirectory,"windows-ocr-report.json"),Path.Combine(outputDirectory,"windows-ocr-error.txt")}))
            if(File.Exists(file))File.Delete(file);
        try
        {
            var config=new Configuration();var vision=new Vision(config);
            string[] screens=["main","menu","destinations","enter","battle","result","reward","battle-live-01","battle-live-02","battle-dither"];
            var rows=new List<object>();int failures=0,checks=0;
            foreach(var screen in screens)
            {
                using var frame=new Bitmap(Path.Combine(screenDirectory,screen+".png"));
                using var annotated=new Bitmap(frame);using var graphics=Graphics.FromImage(annotated);
                foreach(var (target,positives) in Expected)
                {
                    var detection=await vision.Detect(frame,target,CancellationToken.None);
                    var expected=positives.Contains(screen);
                    if(detection.Passed!=expected)failures++;
                    checks++;
                    rows.Add(new {screen,target,expected,detection});
                    using var pen=new Pen(detection.Passed?Color.Lime:Color.OrangeRed,2);
                    graphics.DrawRectangle(pen,config.Targets[target].Region.Rectangle);
                }
                annotated.Save(Path.Combine(outputDirectory,screen+"-regions.png"),System.Drawing.Imaging.ImageFormat.Png);
            }
            File.WriteAllText(Path.Combine(outputDirectory,"windows-ocr-report.json"),JsonSerializer.Serialize(new {
                time=DateTimeOffset.Now,checks,failures,rows,
                limitation="Saved screenshots only. Skip/purchase have negative examples only. This does not validate live input, timing, repeated runs or independent positives."
            },Configuration.JsonOptions));
            return failures==0?0:1;
        }
        catch(Exception error)
        {
            File.WriteAllText(Path.Combine(outputDirectory,"windows-ocr-error.txt"),error.ToString());
            return 2;
        }
    }
}
