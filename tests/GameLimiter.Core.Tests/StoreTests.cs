using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;
namespace GameLimiter.Core.Tests;

public sealed class StoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GameLimiterTests-" + Guid.NewGuid());
    private Store Create() => new(Path.Combine(folder, "test.db"));
    [Fact]
    public void ChildrenCanBeAddedEditedArchivedWithoutLosingHistory()
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); var session = db.Begin(c.Id, "game", DateTime.Now, 5); db.AddUsage(session, DateTime.Now, 120); db.End(session, "Test");
        db.SaveChild(c with { Name = "Renamed", BaseSeconds = 3600 }); Assert.Equal("Renamed", db.Children()[0].Name);
        db.SaveChild(c with { Archived = true, Enabled = false }); Assert.Empty(db.Children()); Assert.Single(db.Children(true)); Assert.Equal(120, db.Allowance(c.Id, DateOnly.FromDateTime(DateTime.Now)).Used);
    }
    [Fact]
    public void UsageAndSettingsSurviveRecreation()
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); db.SaveSettings(new(First: 600)); db.Set("ParentPin", PinService.Hash("1234"));
        foreach (var seconds in new[] { 120, 75 }) { var session = db.Begin(c.Id, "game", DateTime.Now, 5); db.AddUsage(session, DateTime.Now, seconds); db.End(session, "Test"); }
        var reopened = Create(); Assert.Equal(195, reopened.Allowance(c.Id, DateOnly.FromDateTime(DateTime.Now)).Used); Assert.Equal(600, reopened.Settings.First); Assert.True(PinService.Verify("1234", reopened.Get("ParentPin")));
    }
    [Fact]
    public void AdjustmentsAndUsageResetOnNewDate()
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); var yesterday = DateTime.Today.AddDays(-1).AddHours(12); var date = DateOnly.FromDateTime(yesterday);
        db.Adjust(c.Id, date, 600); db.Adjust(c.Id, date, -300); var s = db.Begin(c.Id, "game", yesterday, 5); db.AddUsage(s, yesterday, 900); db.End(s, "Test");
        Assert.Equal(1200, db.Allowance(c.Id, date).Remaining); Assert.Equal(1800, db.Allowance(c.Id, date.AddDays(1)).Remaining); Assert.Equal(0, db.Allowance(c.Id, date.AddDays(1)).Adjustment);
    }
    [Fact]
    public void MidnightSplitsUsage()
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); var start = new DateTime(2026, 9, 7, 23, 59, 50); var s = db.Begin(c.Id, "game", start, 5); db.AddUsage(s, start, 30);
        Assert.Equal(10, db.Allowance(c.Id, new(2026, 9, 7)).Used); Assert.Equal(20, db.Allowance(c.Id, new(2026, 9, 8)).Used); Assert.Equal(30, db.Duration(s));
    }
    [Theory]
    [InlineData("Adult timed")]
    [InlineData("Adult unlimited")]
    public void AdultUsageDoesNotConsumeChild(string mode)
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); var s = db.Begin(null, mode, DateTime.Now, 5); db.AddUsage(s, DateTime.Now, 600); db.End(s, "Test"); Assert.Equal(0, db.Allowance(c.Id, DateOnly.FromDateTime(DateTime.Now)).Used); Assert.Equal(600, db.Duration(s));
    }
    [Fact]
    public void GameAddEditDisableAndRemove()
    {
        var db = Create(); var executable = Environment.ProcessPath!; var game = new Game("test", "Test", executable); db.SaveGame(game); db.SaveGame(game with { DisplayName = "Edited", Enabled = false }); Assert.Equal("Edited", db.Games()[0].DisplayName); Assert.False(db.Games()[0].Enabled); db.RemoveGame("test"); Assert.Empty(db.Games());
    }
    [Fact]
    public async Task InterruptedRecoveryIsBoundedAndIdempotent()
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); var start = DateTime.Today.AddHours(1); var s = db.Begin(c.Id, "game", start, 5); db.AddUsage(s, start, 100);
        var coordinator = new SessionCoordinator(db, new FakeLauncher(), new LocalLog(folder)); await coordinator.RecoverAsync(); await coordinator.RecoverAsync(); Assert.Equal(105, db.Duration(s)); Assert.Empty(db.Interrupted());
    }
    [Fact]
    public async Task CoordinatorAccountsEarlyExitAndRejectsOverlap()
    {
        var db = Create(); var c = db.SaveChild(new(0, "Child", 1800)); var process = new FakeProcess(); var coordinator = new SessionCoordinator(db, new FakeLauncher(process), new LocalLog(folder));
        var run = coordinator.RunAsync(new("test", "Test", "unused"), c.Id, null); Assert.True(coordinator.Active);
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(new("test", "Test", "unused"), c.Id, null)); await Task.Delay(1100); process.HasExited = true; await run;
        Assert.InRange(db.Allowance(c.Id, DateOnly.FromDateTime(DateTime.Now)).Used, 1, 4); Assert.Empty(db.Interrupted()); Assert.False(coordinator.Active);
    }
    [Fact]
    public async Task TimedAdultClosesFakeProcessWithoutChargingChild()
    {
        var db = Create(); var child = db.SaveChild(new(0, "Child", 1800)); var process = new FakeProcess(); var coordinator = new SessionCoordinator(db, new FakeLauncher(process), new LocalLog(folder));
        await coordinator.RunAsync(new("test", "Test", "unused"), null, 1); Assert.True(process.Closed); Assert.Equal(0, db.Allowance(child.Id, DateOnly.FromDateTime(DateTime.Now)).Used);
    }
    [Fact]
    public async Task ExhaustedChildDoesNotLaunch()
    {
        var db = Create(); var child = db.SaveChild(new(0, "Child", 0)); var launcher = new FakeLauncher(); var coordinator = new SessionCoordinator(db, launcher, new LocalLog(folder));
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(new("test", "Test", "unused"), child.Id, null)); Assert.False(launcher.Launched);
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    private sealed class FakeLauncher(FakeProcess? process = null) : IProcessLauncher
    {
        public bool Launched { get; private set; }
        public Task<IGameProcess> LaunchAsync(Game game, int timeoutSeconds, CancellationToken cancellationToken) { Launched = true; return Task.FromResult<IGameProcess>(process ?? new FakeProcess { HasExited = true }); }
    }
    private sealed class FakeProcess : IGameProcess
    {
        public int Id => int.MaxValue; public long StartUtcTicks => 0; public nint WindowHandle => 0; public bool HasExited { get; set; }
        public bool Closed { get; private set; }
        public Task CloseAsync(int graceSeconds, CancellationToken cancellationToken) { Closed = true; HasExited = true; return Task.CompletedTask; }
        public void Dispose() { }
    }
}
