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
