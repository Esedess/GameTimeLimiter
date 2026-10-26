using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Microsoft.Data.Sqlite;
using System.Security;
using Xunit;
namespace GameLimiter.Core.Tests;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class WindowsProfileTests
{
    [Fact]
    public void CurrentUserIsDefaultAndRemoteAccountsAreRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentException>(() => WindowsProfiles.Parse("remote-computer\\child"));
        Assert.Throws<ArgumentException>(() => WindowsProfiles.Parse("child@example.com"));
        Assert.Equal((Environment.MachineName, "child"), WindowsProfiles.Parse(".\\child"));
        var folder = Path.Combine(Path.GetTempPath(), "GLProfile-" + Guid.NewGuid());
        try { var store = new Store(Path.Combine(folder, "test.db")); Assert.Null(WindowsProfiles.Get(store, 1)); WindowsProfiles.Clear(store, 1); Assert.Null(WindowsProfiles.Get(store, 1)); }
        finally { SqliteConnection.ClearAllPools(); Directory.Delete(folder, true); }
    }
    [ProfileFact]
    public async Task RealAlternateUserProfileLaunchDiscoveryAndClose()
    {
        if (!OperatingSystem.IsWindows()) return;
        var folder = Environment.GetEnvironmentVariable("GL_PROFILE_TEST_FOLDER")!;
        var account = Environment.GetEnvironmentVariable("GL_PROFILE_TEST_ACCOUNT")!;
        var secret = Environment.GetEnvironmentVariable("GL_PROFILE_TEST_PASSWORD")!;
        var executable = Environment.GetEnvironmentVariable("GAMELIMITER_TEST_GAME")!;
        using var password = new SecureString(); foreach (var c in secret) password.AppendChar(c); password.MakeReadOnly();
        var store = new Store(Path.Combine(folder, "profile.db")); var child = store.SaveChild(new(0, "Test", 1800));
        WindowsProfiles.Save(store, child.Id, account, password);
        var saved = WindowsProfiles.Get(store, child.Id)!;
        Assert.DoesNotContain(secret, store.Get("WindowsProfile:" + child.Id)!);
        using (var restored = WindowsProfiles.ReadPassword(saved)) Assert.Equal(saved.Sid, WindowsProfiles.Validate(saved.User, saved.Domain, restored));
        using (var wrong = new SecureString()) { wrong.AppendChar('!'); Assert.ThrowsAny<Exception>(() => WindowsProfiles.Save(store, child.Id, account, wrong)); }
        Assert.Equal(saved, WindowsProfiles.Get(store, child.Id));
        var launcher = new ProcessLauncher(new LocalLog(folder), store);
        foreach (var indirect in new[] { false, true })
        {
            var report = Path.Combine(folder, indirect ? "launcher-profile.txt" : "direct-profile.txt");
            var launchPath = executable;
            if (indirect) { launchPath = Path.Combine(Path.GetDirectoryName(executable)!, "GameLimiter.ProfileLauncher.exe"); File.Copy(executable, launchPath, true); }
            var game = new Game("profile-test", "Profile test", launchPath, $"{(indirect ? "--launcher " : "")}--headless --profile-report \"{report}\"", Path.GetDirectoryName(executable)!, indirect ? "GameLimiter.TestGame" : "");
            using var process = await launcher.LaunchForChildAsync(game, child.Id, 15, CancellationToken.None);
            try
            {
                for (var i = 0; i < 100 && !File.Exists(report); i++) await Task.Delay(100);
                var lines = File.ReadAllLines(report);
                Assert.Equal(saved.Sid, lines[0]);
                Assert.Equal(lines[1], lines[2], ignoreCase: true);
                Assert.StartsWith(lines[1], lines[3], StringComparison.OrdinalIgnoreCase);
                Assert.NotEqual(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), lines[1]);
                await process.CloseAsync(1, CancellationToken.None); Assert.True(process.HasExited);
            }
            finally { if (!process.HasExited) await process.CloseAsync(1, CancellationToken.None); }
        }
        WindowsProfiles.Clear(store, child.Id); Assert.Null(WindowsProfiles.Get(store, child.Id));
    }
}
public sealed class ProfileFactAttribute : FactAttribute
{
    public ProfileFactAttribute() { if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GL_PROFILE_TEST_PASSWORD"))) Skip = "Requires explicitly authorized temporary Windows account; run tools/Test-WindowsProfile.ps1 elevated."; }
}
