using Xunit;
namespace AbyssAuto.Tests;
public class DecisionTests
{
    [Theory]
    [InlineData("화면을 터치해 주세요","터치해",true)]
    [InlineData("화면을터치해 주세요","화면을 터치해 주세요",true)]
    [InlineData("확인","터치해",false)]
    [InlineData("나가기","다시 하기",false)]
    [InlineData("다른 던전 가기","다시 하기",false)]
    [InlineData("다시 하기","다시 하기",true)]
    [InlineData("불안정","다시 시도하기",false)]
    public void StrictRecognition(string actual,string expected,bool pass)=>Assert.Equal(pass,TextRules.Matches(actual,expected));
    [Fact] public void FuzzyIsOptIn()
    {
        Assert.False(TextRules.Matches("입장하가","입장하기"));
        Assert.True(TextRules.Matches("입장하가","입장하기",true));
        Assert.False(TextRules.Matches("구미","구매",true));
    }
    [Fact] public void MissingButtonIsNotTransitionSuccess()
    {
        var gate=new TransitionGate(true,3);
        for(int i=0;i<100;i++)Assert.False(gate.Observe(false,false));
    }
    [Fact] public void OneFrameAndLoadingDoNotConfirmBattle()
    {
        var gate=new TransitionGate(true,3);
        Assert.False(gate.Observe(false,true));
        Assert.False(gate.Observe(false,false));
        Assert.False(gate.Observe(false,true));
        Assert.True(gate.Observe(false,true));
    }
    [Fact] public void OldButtonAndBattleConflictCannotSucceed()
    {
        var gate=new TransitionGate(true,3);
        for(int i=0;i<10;i++)Assert.False(gate.Observe(true,true));
        Assert.False(gate.Observe(false,true));Assert.True(gate.Observe(false,true));
    }
    [Fact] public void InputIsBoundedToInitialPlusThreeRetries()
    {
        var gate=new TransitionGate(false,3);
        for(int i=0;i<4;i++)gate.BeforeInput();
        Assert.Throws<InvalidOperationException>(()=>gate.BeforeInput());Assert.Equal(4,gate.Attempts);
    }
    [Fact] public void MenuMayRemainWhenDestinationScreenAppears()
    {
        var gate=new TransitionGate(false,3);
        Assert.False(gate.Observe(true,true));Assert.True(gate.Observe(true,true));
    }
}

public class WhiteTextTests
{
    [Theory]
    [InlineData(255,255,255,true)]
    [InlineData(205,240,220,true)]
    [InlineData(14,176,115,false)]
    [InlineData(0,0,0,false)]
    [InlineData(180,180,180,false)]
    [InlineData(190,250,190,true)]
    [InlineData(190,251,190,false)]
    public void WhiteMaskPreservesTextAndRejectsButtonBackground(int r,int g,int b,bool expected)
        =>Assert.Equal(expected,WhiteTextRules.IsGlyph((byte)r,(byte)g,(byte)b));
}

public class BattleEvidenceTests
{
    [Theory]
    [InlineData("허상의 정박지 클리|부활 6회 이내로 클리|",true)]
    [InlineData("허상의 정박지 들리|부활 6회 이내로 결」",true)]
    [InlineData("허상의 정박지 클리어",false)]
    [InlineData("부활 6회 이내로 클리어",false)]
    [InlineData("광기의 동굴 클리어|부활 6회",false)]
    [InlineData("소환 결계 퇴치|말하기",false)]
    [InlineData("허상의 정박지|여기서 부활",false)]
    public void RequireObjectiveAndDungeonSubgoal(string ocr,bool expected)
        =>Assert.Equal(expected,RecognitionRules.HasTextEvidence(ocr.Split('|'),["허상의 정박지"],["부활 6회"]));
    [Fact] public void ReplayActualFailedBattleOcr()
    {
        using var fixture=System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"live-battle-ocr.json")));
        var lines=fixture.RootElement.GetProperty("attempts").EnumerateArray().Select(x=>x.GetProperty("Text").GetString()!).ToArray();
        Assert.DoesNotContain(lines,x=>TextRules.Matches(x,"허상의 정박지 클리어"));
        Assert.True(RecognitionRules.HasTextEvidence(lines,["허상의 정박지"],["부활 6회"]));
    }
}

public class StartStageTests
{
    [Fact] public void InitialStartAllowsConfirmedMain()=>Assert.Equal(Stage.Menu,StartStagePolicy.Resolve(false,[],true));
    [Fact] public void OpenMenuDoesNotNeedEscape()=>Assert.Equal(Stage.Abyss,StartStagePolicy.Resolve(false,[Stage.Abyss],false));
    [Fact] public void InitialStartCannotOpenMenuDuringBattle()=>Assert.Throws<InvalidOperationException>(()=>StartStagePolicy.Resolve(false,[Stage.Battle],true));
    [Fact] public void ResumeUsesBattleStage()=>Assert.Equal(Stage.Battle,StartStagePolicy.Resolve(true,[Stage.Battle],true));
    [Fact] public void UnknownResumeDoesNotResetFromChat()=>Assert.Throws<InvalidOperationException>(()=>StartStagePolicy.Resolve(true,[],true));
    [Fact] public void ConflictingScreensNeverStart()=>Assert.Throws<InvalidOperationException>(()=>StartStagePolicy.Resolve(true,[Stage.Battle,Stage.Retry],true));
    [Fact] public void UnrecognizedInitialScreenStops()=>Assert.Throws<InvalidOperationException>(()=>StartStagePolicy.Resolve(false,[],false));
}
