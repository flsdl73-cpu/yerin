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

public class DitherDetectionTests
{
    [Theory]
    [InlineData("battle",false)]
    [InlineData("battle-live-01",false)]
    [InlineData("battle-live-02",false)]
    [InlineData("battle-dither",true)]
    public void DetectActualGridWithoutFilteringNormalBattleFrames(string name,bool expected)
    {
        var pixels=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"fixtures",name+".gray"));
        Assert.Equal(expected,DitherRules.IsDithered(pixels,252,60));
    }
    [Fact] public void DetectingGridDoesNotSupplyBattleEvidence()
        =>Assert.False(RecognitionRules.HasTextEvidence(["XXXXXX"],["허상의 정박지"],["부활 6회"]));
    [Fact] public void RejectMalformedImage()=>Assert.Throws<ArgumentException>(()=>DitherRules.Ratio(new byte[4],3,3));
}

public class RepeatRouteTests
{
    [Fact]public void DefaultIsVerifiedSoloRoute()=>Assert.Equal(PlayMode.Solo,new RunOptions().PlayMode);
    [Fact]public void SoloKeepsRetryAndMultiUsesOtherDungeon()
    {
        Assert.Equal("retry",RepeatRoute.RewardTarget(PlayMode.Solo));Assert.Equal(Stage.Retry,RepeatRoute.RewardStage(PlayMode.Solo));
        Assert.Equal("other_dungeon",RepeatRoute.RewardTarget(PlayMode.TwoOrMore));Assert.Equal(Stage.OtherDungeon,RepeatRoute.RewardStage(PlayMode.TwoOrMore));
    }
    [Theory][InlineData(true,false,PlayMode.Solo)][InlineData(false,true,PlayMode.TwoOrMore)]
    public void ExactlyOneSelection(bool solo,bool group,PlayMode expected)=>Assert.Equal(expected,RunOptions.FromChecks(solo,group));
    [Theory][InlineData(true,true)][InlineData(false,false)]
    public void RejectConflictingOrEmptySelection(bool solo,bool group)=>Assert.Throws<InvalidOperationException>(()=>RunOptions.FromChecks(solo,group));
    [Fact]public void RewardResumeUsesOnlySelectedAction()
    {
        Assert.Equal(Stage.OtherDungeon,StartStagePolicy.Resolve(true,[RepeatRoute.RewardStage(PlayMode.TwoOrMore)],false));
        Assert.Throws<InvalidOperationException>(()=>StartStagePolicy.Resolve(false,[Stage.OtherDungeon],false));
    }
}

public class WindowsInputPacketTests
{
    [Fact]public void Win64InputLayoutMatchesWindowsApi()
    {
        Assert.Equal(40,System.Runtime.InteropServices.Marshal.SizeOf<WindowsInputPacket>());
        Assert.Equal(8,System.Runtime.InteropServices.Marshal.OffsetOf<WindowsInputPacket>(nameof(WindowsInputPacket.Keyboard)).ToInt32());
    }
    [Theory][InlineData(0x01)][InlineData(0x39)]
    public void ScanCodeDownAndUpHaveCorrectFlagsAndMarker(int scan)
    {
        var down=WindowsInputPackets.Key((ushort)scan,false);var up=WindowsInputPackets.Key((ushort)scan,true);
        Assert.Equal(1u,down.Type);Assert.Equal((ushort)scan,down.Keyboard.ScanCode);Assert.Equal((ushort)0,down.Keyboard.VirtualKey);
        Assert.Equal(8u,down.Keyboard.Flags);Assert.Equal(10u,up.Keyboard.Flags);Assert.Equal(WindowsInputPackets.Marker,up.Keyboard.ExtraInfo);
    }
    [Fact]public void MouseReleaseDoesNotMoveCursor()
    {Assert.Equal(2u,WindowsInputPackets.MouseButton(false).Mouse.Flags);Assert.Equal(4u,WindowsInputPackets.MouseButton(true).Mouse.Flags);}
    [Fact]public void MouseMoveUsesAbsolutePrimaryMonitorCoordinates()
    {var p=WindowsInputPackets.Move(10000,20000);Assert.Equal(0x8001u,p.Mouse.Flags);Assert.Equal(10000,p.Mouse.X);Assert.Equal(20000,p.Mouse.Y);Assert.Equal(WindowsInputPackets.Marker,p.Mouse.ExtraInfo);}
}

public class RevivalPolicyTests
{
    [Fact]public void DefaultDoesNotEnableUnverifiedRevival()=>Assert.False(new RunOptions().AutoRevive);
    [Theory][InlineData(true,false)][InlineData(false,true)][InlineData(false,false)]
    public void NeverClickWithoutBothDeathAndButton(bool dead,bool button)
        =>Assert.Equal(RevivalAction.Wait,new RevivalRules().Observe(dead,button,false,false,null,true,1,0).Action);
    [Fact]public void FourSecondIntervalAndSameFrameNeverDoubleClick()
    {
        var p=new RevivalRules();Assert.Equal(RevivalAction.Click,p.Observe(true,true,false,false,6,true,1,0).Action);
        Assert.Equal(0,p.Attempts);p.Sent(1,0);Assert.Equal(1,p.Attempts);
        Assert.Equal(RevivalAction.Wait,p.Observe(true,true,false,false,6,true,1,10).Action);
        Assert.Equal(RevivalAction.Wait,p.Observe(true,true,false,false,6,true,2,3.999).Action);
        Assert.Equal(RevivalAction.Click,p.Observe(true,true,false,false,6,true,3,4).Action);
    }
    [Fact]public void MissingDeathTextAloneDoesNotConfirmRecovery()
    {
        var p=new RevivalRules();Assert.Equal(RevivalAction.Wait,p.Observe(false,true,true,false,null,true,1,0).Action);
        Assert.Equal(RevivalAction.Wait,p.Observe(false,false,true,false,null,true,2,1).Action);
        Assert.Equal(RevivalAction.Wait,p.Observe(false,false,true,false,null,true,2,1).Action);
        Assert.Equal(RevivalAction.Recovered,p.Observe(false,false,true,false,null,true,3,2).Action);
    }
    [Fact]public void LoadingOrUnknownScreenDoesNotMeanRevived()
    {var p=new RevivalRules();for(int i=1;i<5;i++)Assert.Equal(RevivalAction.Wait,p.Observe(false,false,false,false,null,true,i,i).Action);}
    [Fact]public void StaleFramesCannotSupplyClick()
    {var p=new RevivalRules();Assert.Equal(RevivalAction.Wait,p.Observe(true,true,false,false,6,false,1,0).Action);Assert.Throws<InvalidOperationException>(()=>p.Sent(1,0));}
    [Fact]public void PurchaseOrZeroQuotaAlwaysStops()
    {
        Assert.Equal(RevivalAction.Stop,new RevivalRules().Observe(true,true,false,true,null,true,1,0).Action);
        Assert.Equal(RevivalAction.Stop,new RevivalRules().Observe(true,true,false,false,0,true,1,0).Action);
    }
    [Fact]public void InputRetriesAreBoundedAndWaitBeforeFinalFailure()
    {
        var p=new RevivalRules(1);Assert.Equal(RevivalAction.Click,p.Observe(true,true,false,false,6,true,1,0).Action);p.Sent(1,0);
        Assert.Equal(RevivalAction.Wait,p.Observe(true,true,false,false,6,true,2,1).Action);
        Assert.Equal(RevivalAction.Stop,p.Observe(true,true,false,false,6,true,3,4).Action);
    }
    [Theory][InlineData("남은 부활 횟수 3/3",3)][InlineData("남은 부활 횟수 6/6",6)][InlineData("남은 부활 횟수 0/6",0)]
    public void ReadActualQuotaLabels(string text,int expected)=>Assert.Equal(expected,RevivalRules.Remaining(text));
    [Theory][InlineData("1 여기서 부활")][InlineData("남은 부활 횟수 O/6")][InlineData("남은 부활 횟수 7/6")]
    public void CostAndUnreadableTextAreNotQuota(string text)=>Assert.Null(RevivalRules.Remaining(text));
    [Theory][InlineData(255,153,48,true)][InlineData(237,176,52,true)][InlineData(14,176,115,false)][InlineData(255,0,0,false)][InlineData(255,255,255,false)]
    public void SeparateOrangeReviveFromGreenOtherActions(int r,int g,int b,bool expected)=>Assert.Equal(expected,RevivalColor.IsOrange(r,g,b));
}

public class ExactReviveTextTests
{
    [Fact]public void LocateOnlyHereReviveWordsWhenOcrMergesButtons()
    {
        TextHit[] words=[new("여신상에서",new(900,750,100,24)),new("부활",new(1005,750,40,24)),new("1",new(1110,750,10,24)),new("여기서",new(1145,750,55,24)),new("부활",new(1206,750,40,24))];
        var match=Assert.Single(OcrTextGroups.ExactPhrases(words,["여기서 부활"]));
        Assert.Equal(1145,match.Bounds.Left);Assert.Equal(1246,match.Bounds.Right);
    }
    [Fact]public void GrowthGuideAndGoddessDoNotMatch()
    {
        TextHit[] words=[new("여신상에서",new(900,750,100,24)),new("부활",new(1005,750,40,24)),new("성장",new(1120,750,45,24)),new("가이드",new(1170,750,60,24))];
        Assert.Empty(OcrTextGroups.ExactPhrases(words,["여기서 부활"]));
    }
    [Fact]public void DoNotJoinFarApartLabelsIntoClickTarget()
    {Assert.Empty(OcrTextGroups.ExactPhrases([new("여기서",new(900,750,50,24)),new("부활",new(1200,750,40,24))],["여기서 부활"]));}
}
