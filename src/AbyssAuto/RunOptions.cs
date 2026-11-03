using System.Text.Json;
using System.Text.Json.Serialization;
namespace AbyssAuto;

public enum PlayMode { Solo,TwoOrMore }
public sealed class RunOptions
{
    public PlayMode PlayMode { get; set; }=PlayMode.Solo;
    private static readonly JsonSerializerOptions Json=new() { WriteIndented=true,Converters={new JsonStringEnumConverter(allowIntegerValues:false)} };
    public static string FilePath=>Path.Combine(AppContext.BaseDirectory,"run-options.json");
    public static RunOptions Load()
    {
        if(!File.Exists(FilePath))return new();
        if(new FileInfo(FilePath).Length>4096)throw new InvalidDataException("실행 유형 설정 파일이 너무 큽니다.");
        var value=JsonSerializer.Deserialize<RunOptions>(File.ReadAllText(FilePath),Json)??throw new InvalidDataException("실행 유형 설정이 비어 있습니다.");
        if(!Enum.IsDefined(value.PlayMode))throw new InvalidDataException("알 수 없는 실행 유형입니다.");
        return value;
    }
    public void Save()
    {
        if(!Enum.IsDefined(PlayMode))throw new InvalidDataException("알 수 없는 실행 유형입니다.");
        var temporary=FilePath+".tmp";
        try {File.WriteAllText(temporary,JsonSerializer.Serialize(this,Json));File.Move(temporary,FilePath,true);}
        finally {if(File.Exists(temporary))File.Delete(temporary);}
    }
    public static PlayMode FromChecks(bool solo,bool twoOrMore)=>solo!=twoOrMore?(solo?PlayMode.Solo:PlayMode.TwoOrMore):throw new InvalidOperationException("혼자하기와 2인이상 중 하나를 선택해주세요.");
    public static string Label(PlayMode mode)=>mode==PlayMode.Solo?"혼자하기":"2인이상";
}

public static class RepeatRoute
{
    public static string RewardTarget(PlayMode mode)=>mode switch {
        PlayMode.Solo=>"retry",PlayMode.TwoOrMore=>"other_dungeon",_=>throw new InvalidOperationException("알 수 없는 실행 유형입니다.") };
    public static Stage RewardStage(PlayMode mode)=>mode switch {
        PlayMode.Solo=>Stage.Retry,PlayMode.TwoOrMore=>Stage.OtherDungeon,_=>throw new InvalidOperationException("알 수 없는 실행 유형입니다.") };
}
