using System.Text.Json;
namespace AbyssAuto;

public enum Stage { Menu, Abyss, Destination, Enter, Battle, Result, Retry }
public sealed record Region(int Left, int Top, int Right, int Bottom)
{
    public Rectangle Rectangle => Rectangle.FromLTRB(Left, Top, Right, Bottom);
}
public sealed class Target
{
    public Region Region { get; set; } = new(0, 0, 1, 1);
    public string[] Text { get; set; } = [];
    public string[] Images { get; set; } = [];
    public double Threshold { get; set; } = .75;
    public int MinimumScore { get; set; } = 2;
    public string Color { get; set; } = "none";
    public bool RequireText { get; set; }
    public bool RequireImage { get; set; }
    public bool RequireColor { get; set; }
    public bool Fuzzy { get; set; }
}
public sealed class Scenario
{
    public int Width { get; set; } = 1297;
    public int Height { get; set; } = 873;
    public int ButtonTimeoutSeconds { get; set; } = 25;
    public int RetryTimeoutSeconds { get; set; } = 60;
    public int BattleTimeoutSeconds { get; set; } = 600;
    public int UnknownTimeoutSeconds { get; set; } = 15;
    public int MaximumRetries { get; set; } = 3;
    public string WindowTitle { get; set; } = "마비노기 모바일";
}
public sealed class Configuration
{
    public static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true, WriteIndented = true };
    public Scenario Scenario { get; }
    public Dictionary<string, Target> Targets { get; }
    public string Root { get; } = AppContext.BaseDirectory;
    public Configuration()
    {
        Scenario = JsonSerializer.Deserialize<Scenario>(File.ReadAllText(Path.Combine(Root, "scenario.json")), JsonOptions)!;
        Targets = JsonSerializer.Deserialize<Dictionary<string, Target>>(File.ReadAllText(Path.Combine(Root, "targets.json")), JsonOptions)!;
        if (Scenario.MaximumRetries is < 0 or > 3) throw new InvalidDataException("재시도는 0~3회여야 합니다.");
        foreach (var (name, t) in Targets)
        {
            var r = t.Region;
            if (r.Left < 0 || r.Top < 0 || r.Right > Scenario.Width || r.Bottom > Scenario.Height || r.Right <= r.Left || r.Bottom <= r.Top)
                throw new InvalidDataException($"{name}: 인식 영역 오류");
            if (t.Threshold is < 0 or > 1) throw new InvalidDataException($"{name}: 사진 기준 오류");
        }
    }
}
