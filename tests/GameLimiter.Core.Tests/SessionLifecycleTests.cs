using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;
namespace GameLimiter.Core.Tests;

public sealed class SessionLifecycleTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GameLimiterLifecycle-" + Guid.NewGuid());
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AddTimeExtendsCurrentSession(bool childSession)
    {
        var store = new Store(Path.Combine(folder, "test.db"));
        var child = store.SaveChild(new(0, "Child", 1800));
        var coordinator = new SessionCoordinator(store, new Launcher(new TestProcess()), new LocalLog(folder));
        var added = false;
        coordinator.Updated += (_, state, _) =>
        {
            if (state != SessionState.Running) return;
            if (!added)
            {
                Assert.Throws<InvalidOperationException>(() => coordinator.AddTime(coordinator.SessionId + 1, 5));
                Assert.Throws<ArgumentException>(() => coordinator.AddTime(coordinator.SessionId, 0));
                Assert.Throws<ArgumentException>(() => coordinator.AddTime(coordinator.SessionId, 1441));
                coordinator.AddTime(coordinator.SessionId, 5); added = true;
            }
            else { Assert.InRange(coordinator.Remaining, 2090, 2100); coordinator.Stop(); }
        };
        await coordinator.RunAsync(new("test", "Test", "unused.exe"), childSession ? child.Id : null, childSession ? null : 1800);
        Assert.True(added);
        Assert.Equal(childSession ? 300 : 0, store.Allowance(child.Id, DateOnly.FromDateTime(DateTime.Now)).Adjustment);
        Assert.Equal(1800, store.Children()[0].BaseSeconds);
        Assert.Throws<InvalidOperationException>(() => coordinator.AddTime(coordinator.SessionId, 5));
    }
    [Fact]
    public async Task AddingTimeCancelsAdjustmentGrace()
    {
        var store = new Store(Path.Combine(folder, "test.db")); var child = store.SaveChild(new(0, "Child", 60));
        var coordinator = new SessionCoordinator(store, new Launcher(new TestProcess()), new LocalLog(folder));
        var step = 0;
        coordinator.Updated += (_, state, _) =>
        {
            if (state == SessionState.Running && step == 0) { store.Adjust(child.Id, DateOnly.FromDateTime(DateTime.Now), -60); coordinator.SettingsChanged(true); step = 1; }
            else if (state == SessionState.GraceCountdown && step == 1) { coordinator.AddTime(coordinator.SessionId, 5); step = 2; }
            else if (state == SessionState.Running && step == 2) { step = 3; coordinator.Stop(); }
        };
        await coordinator.RunAsync(new("test", "Test", "unused.exe"), child.Id, null);
        Assert.Equal(3, step);
    }
    [Fact]
    public async Task UnlimitedAndClosingSessionsCannotBeExtended()
    {
        var store = new Store(Path.Combine(folder, "test.db"));
        var coordinator = new SessionCoordinator(store, new Launcher(new TestProcess()), new LocalLog(folder));
        coordinator.Updated += (_, state, _) => { if (state == SessionState.Running) { Assert.False(coordinator.CanAddTime); Assert.Throws<InvalidOperationException>(() => coordinator.AddTime(coordinator.SessionId, 5)); coordinator.Stop(); } };
        await coordinator.RunAsync(new("test", "Test", "unused.exe"), null, null);
        var closing = new SessionCoordinator(store, new Launcher(new TestProcess()), new LocalLog(folder));
        closing.Updated += (_, state, _) => { if (state == SessionState.Closing) Assert.Throws<InvalidOperationException>(() => closing.AddTime(closing.SessionId, 5)); };
        await closing.RunAsync(new("test", "Test", "unused.exe"), null, 0);
    }
    [Fact]
    public async Task StartedIsRaisedAfterAttachmentAndReturnFollowsFinalPersistence()
    {
        var store = new Store(Path.Combine(folder, "test.db"));
        var process = new TestProcess();
        var coordinator = new SessionCoordinator(store, new Launcher(process), new LocalLog(folder));
        var started = 0;
        coordinator.Started += () =>
        {
            started++;
            Assert.True(coordinator.Active);
            Assert.Equal(process.Id, Assert.Single(store.Interrupted()).Pid);
            coordinator.Stop();
        };
        await coordinator.RunAsync(new("test", "Test", "unused.exe"), null, null);
        Assert.Equal(1, started); Assert.True(process.HasExited); Assert.True(process.Disposed);
        Assert.False(coordinator.Active); Assert.Empty(store.Interrupted());
    }
    [Fact]
    public async Task FailedLaunchDoesNotRaiseStartedAndPropagatesError()
    {
        var store = new Store(Path.Combine(folder, "test.db"));
        var coordinator = new SessionCoordinator(store, new Launcher(null), new LocalLog(folder));
        var started = false; coordinator.Started += () => started = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => coordinator.RunAsync(new("test", "Test", "unused.exe"), null, null));
        Assert.False(started); Assert.False(coordinator.Active);
    }
    private sealed class Launcher(TestProcess? process) : IProcessLauncher
    {
        public Task<IGameProcess> LaunchAsync(Game game, int timeoutSeconds, CancellationToken cancellationToken)
            => process is null ? Task.FromException<IGameProcess>(new IOException("Launch failed")) : Task.FromResult<IGameProcess>(process);
    }
    private sealed class TestProcess : IGameProcess
    {
        public int Id => 123;
        public long StartUtcTicks => DateTime.UtcNow.Ticks;
        public nint WindowHandle => 0;
        public bool HasExited { get; private set; }
        public bool Disposed { get; private set; }
        public Task CloseAsync(int graceSeconds, CancellationToken cancellationToken) { HasExited = true; return Task.CompletedTask; }
        public void Dispose() => Disposed = true;
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(folder)) Directory.Delete(folder, true); }
}
