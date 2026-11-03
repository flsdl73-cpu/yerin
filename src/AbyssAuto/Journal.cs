using System.Text.Json;
namespace AbyssAuto;
public sealed class Journal
{
    private readonly string folder=Path.Combine(AppContext.BaseDirectory,"logs");
    public event Action<string>? Message;
    public string LastInput { get; private set; }="없음";
    public void Write(string message, object? data=null)
    {
        Directory.CreateDirectory(folder);
        var file=Path.Combine(folder,"events.jsonl");
        if(File.Exists(file) && new FileInfo(file).Length>2_000_000)
        {
            for(var i=3;i>=1;i--) { var old=Path.Combine(folder,$"events.{i}.jsonl"); if(i==3) {if(File.Exists(old)) File.Delete(old);} else if(File.Exists(old)) File.Move(old,Path.Combine(folder,$"events.{i+1}.jsonl"),true); }
            File.Move(file,Path.Combine(folder,"events.1.jsonl"),true);
        }
        File.AppendAllText(file,JsonSerializer.Serialize(new {time=DateTimeOffset.Now,message,data})+Environment.NewLine);
        Message?.Invoke($"{DateTime.Now:HH:mm:ss}  {message}");
    }
    public void Input(string description) { LastInput=$"{DateTimeOffset.Now:O} {description}"; Write("입력 시도",LastInput); }
    public void Failure(Bitmap? frame,Stage stage,Exception error,object? evidence)
    {
        var captures=Path.Combine(AppContext.BaseDirectory,"captures");Directory.CreateDirectory(captures);
        var stem=Path.Combine(captures,DateTime.Now.ToString("yyyyMMdd-HHmmss-fff"));
        frame?.Save(stem+".png",System.Drawing.Imaging.ImageFormat.Png);
        File.WriteAllText(stem+".json",JsonSerializer.Serialize(new {stage=stage.ToString(),reason=error.Message,lastInput=LastInput,evidence},Configuration.JsonOptions));
        foreach(var f in new DirectoryInfo(captures).GetFiles().OrderByDescending(f=>f.LastWriteTimeUtc).Skip(80)) f.Delete();
        Write($"오류 정지: {error.Message}");
    }
}
