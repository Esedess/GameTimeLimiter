using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Microsoft.Data.Sqlite;
using Xunit;
namespace GameLimiter.Core.Tests;

public sealed class SessionLifecycleTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "GameLimiterLifecycle-" + Guid.NewGuid());
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
