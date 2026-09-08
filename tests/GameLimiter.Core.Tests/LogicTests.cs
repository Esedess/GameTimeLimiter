using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Xunit;
namespace GameLimiter.Core.Tests;

public sealed class LogicTests
{
    [Theory]
    [InlineData(30, 0, 0, 30)]
    [InlineData(30, 0, 10, 20)]
    [InlineData(30, 15, 10, 35)]
    [InlineData(30, -10, 7, 13)]
    [InlineData(30, -20, 25, 0)]
    [InlineData(30, -100, 0, 0)]
    public void AllowanceExamples(int minutes, int adjustment, int used, int expected) => Assert.Equal(expected * 60, new Allowance(minutes * 60, adjustment * 60, used * 60).Remaining);
    [Fact] public void EffectiveLimitCannotBeNegative() => Assert.Equal(0, new Allowance(30, -100, 0).Effective);
    [Fact]
    public void ReductionStartsGraceAndAddedTimeCancelsIt()
    {
        var timer = new SessionTimer(); Assert.Equal(60, timer.Evaluate(0, 10, true, 60)); Assert.Equal(SessionState.GraceCountdown, timer.State);
        Assert.Equal(30, timer.Evaluate(0, 40, false, 60)); Assert.Equal(300, timer.Evaluate(300, 41, false, 60)); Assert.Equal(SessionState.Running, timer.State);
        Assert.Equal(100, timer.Evaluate(100, 42, true, 60)); Assert.Equal(SessionState.Running, timer.State);
    }
    [Fact]
    public void GraceEndsWithoutRestarting()
    {
        var timer = new SessionTimer(); timer.Evaluate(0, 0, true, 60); Assert.Equal(1, timer.Evaluate(0, 59, true, 60)); Assert.Equal(0, timer.Evaluate(0, 60, false, 60)); Assert.Equal(SessionState.Closing, timer.State);
    }
    [Fact] public void NaturalExpiryClosesImmediately() { var timer = new SessionTimer(); Assert.Equal(0, timer.Evaluate(0, 30, false, 60)); Assert.Equal(SessionState.Closing, timer.State); }
    [Fact] public void SettingsValidation() { new TimerSettings().Validate(); Assert.Throws<ArgumentException>(() => new TimerSettings(First: 30).Validate()); Assert.Throws<ArgumentException>(() => new TimerSettings(Checkpoint: 0).Validate()); Assert.Throws<ArgumentException>(() => new TimerSettings(CloseGrace: 61).Validate()); }
    [Fact] public void ChildValidation() { Assert.Throws<ArgumentException>(() => Validation.Child(new(0, " ", 1800))); Assert.Throws<ArgumentException>(() => Validation.Child(new(0, "Child", -1))); }
    [Fact] public void GameValidation() { Assert.Throws<ArgumentException>(() => Validation.Game(new("bad id", "Game", "missing.exe"))); Assert.Throws<ArgumentException>(() => Validation.Game(new("game", "Game", Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".exe")))); }
    [Fact]
    public void PinHashVerifyAndChange()
    {
        var hash = PinService.Hash("1234"); Assert.NotEqual("1234", hash); Assert.True(PinService.Verify("1234", hash)); Assert.False(PinService.Verify("1111", hash));
        var changed = PinService.Hash("9876"); Assert.False(PinService.Verify("1234", changed)); Assert.True(PinService.Verify("9876", changed)); Assert.False(PinService.Verify("1234", "invalid")); Assert.Throws<ArgumentException>(() => PinService.Hash("12"));
    }
}
