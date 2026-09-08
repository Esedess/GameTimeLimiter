using System.Diagnostics;
using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Xunit;
namespace GameLimiter.Core.Tests;

// Explicit opt-in: publish the helper and set GAMELIMITER_TEST_GAME before running this class.
public sealed class ProcessIntegrationTests
{
    [ProcessFact]
    public async Task RealLauncherDiscoveryAndForcedClose()
    {
        var executable = Environment.GetEnvironmentVariable("GAMELIMITER_TEST_GAME");
        Assert.False(string.IsNullOrEmpty(executable));
        Assert.True(File.Exists(executable));
        var directory = Path.GetDirectoryName(executable)!;
        var launcherPath = Path.Combine(directory, "GameLimiter.TestLauncher.exe");
        File.Copy(executable, launcherPath, true);
        var log = new LocalLog(Path.Combine(directory, "logs"));
        var launcher = new ProcessLauncher(log);
        using var game = await launcher.LaunchAsync(new("test", "Test", launcherPath, "--launcher --headless", directory, "GameLimiter.TestGame"), 10, CancellationToken.None);
        try { Assert.False(game.HasExited); await game.CloseAsync(1, CancellationToken.None); Assert.True(game.HasExited); }
        finally { if (!game.HasExited) await game.CloseAsync(1, CancellationToken.None); }
    }
    [ProcessFact]
    public async Task ExistingUnrelatedProcessIsNotAdopted()
    {
        var executable = Environment.GetEnvironmentVariable("GAMELIMITER_TEST_GAME");
        Assert.False(string.IsNullOrEmpty(executable));
        var directory = Path.GetDirectoryName(executable)!;
        using var unrelated = Process.Start(new ProcessStartInfo(executable, "--headless") { UseShellExecute = false, WindowStyle = ProcessWindowStyle.Hidden })!;
        try
        {
            var copy = Path.Combine(directory, "GameLimiter.TestOther.exe"); File.Copy(executable, copy, true);
            var launcher = new ProcessLauncher(new LocalLog(Path.Combine(directory, "logs")));
            await Assert.ThrowsAsync<TimeoutException>(() => launcher.LaunchAsync(new("test", "Test", copy, "--headless", directory, "GameLimiter.TestGame"), 1, CancellationToken.None));
            Assert.False(unrelated.HasExited);
        }
        finally { if (!unrelated.HasExited) unrelated.Kill(true); await unrelated.WaitForExitAsync(); }
    }
}
public sealed class ProcessFactAttribute : FactAttribute
{
    public ProcessFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GAMELIMITER_TEST_GAME"))) Skip = "Set GAMELIMITER_TEST_GAME to the published test helper to run Windows process integration tests.";
    }
}
