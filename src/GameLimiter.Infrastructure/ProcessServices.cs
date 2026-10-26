using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using GameLimiter.Core;
namespace GameLimiter.Infrastructure;

public sealed class ProcessLauncher(LocalLog log, Store? store = null) : IChildProcessLauncher
{
    public Task<IGameProcess> LaunchAsync(Game game, int timeoutSeconds, CancellationToken cancellationToken) => LaunchForChildAsync(game, null, timeoutSeconds, cancellationToken);
    public async Task<IGameProcess> LaunchForChildAsync(Game game, long? child, int timeoutSeconds, CancellationToken cancellationToken)
    {
        Validation.Game(game);
        WindowsProfile? profile = OperatingSystem.IsWindows() && store is not null && child is long id ? WindowsProfiles.Get(store, id) : null;
        if (profile is not null && OperatingSystem.IsWindows() && !WindowsProfiles.IsElevated())
            throw new InvalidOperationException("Для контроля игры другого пользователя запустите GameLimiter Beta от имени администратора (того же пользователя, который сохранил пароль).");
        var expected = string.IsNullOrWhiteSpace(game.TrackedProcessName) ? Path.GetFileNameWithoutExtension(game.LaunchExecutable) : game.TrackedProcessName;
        if (expected.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) expected = expected[..^4];
        var baseline = new HashSet<int>();
        foreach (var p in Process.GetProcessesByName(expected)) { using (p) baseline.Add(p.Id); }
        var started = DateTime.UtcNow;
        var info = new ProcessStartInfo(game.LaunchExecutable, game.LaunchArguments) { UseShellExecute = false, WorkingDirectory = string.IsNullOrWhiteSpace(game.WorkingDirectory) ? Path.GetDirectoryName(game.LaunchExecutable)! : game.WorkingDirectory };
        Process launched;
        if (profile is not null && OperatingSystem.IsWindows())
        {
            using var password = WindowsProfiles.ReadPassword(profile);
            if (WindowsProfiles.Validate(profile.User, profile.Domain, password) != profile.Sid) throw new InvalidOperationException("Учётная запись Windows изменилась. Сохраните профиль ребёнка заново.");
            info.UserName = profile.User; info.Domain = profile.Domain; info.Password = password; info.LoadUserProfile = true;
            // Do not touch info.Environment: Windows must build the target user's environment.
            launched = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить игру под выбранным пользователем.");
        }
        else launched = Process.Start(info) ?? throw new InvalidOperationException("Не удалось запустить игру.");
        log.Write($"Launch {game.Id}, pid={launched.Id}");
        if (string.Equals(expected, Path.GetFileNameWithoutExtension(game.LaunchExecutable), StringComparison.OrdinalIgnoreCase)) return new ManagedProcess(launched, log);
        try
        {
            var deadline = Stopwatch.StartNew();
            while (deadline.Elapsed.TotalSeconds < timeoutSeconds)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var candidates = new List<Process>();
                foreach (var p in Process.GetProcessesByName(expected))
                {
                    try { if (!baseline.Contains(p.Id) && p.StartTime.ToUniversalTime() >= started && p.SessionId == Process.GetCurrentProcess().SessionId && (profile is null || (OperatingSystem.IsWindows() && WindowsProfiles.OwnedBy(p, profile.Sid)))) candidates.Add(p); else p.Dispose(); }
                    catch (InvalidOperationException) { p.Dispose(); }
                    catch (System.ComponentModel.Win32Exception) { p.Dispose(); }
                }
                if (candidates.Count == 1) { log.Write($"Discovered {expected}, pid={candidates[0].Id}"); return new ManagedProcess(candidates[0], log); }
                if (candidates.Count > 1) { foreach (var p in candidates) p.Dispose(); throw new InvalidOperationException("Найдено несколько новых игровых процессов. Закройте их и повторите запуск."); }
                await Task.Delay(250, cancellationToken);
            }
            throw new TimeoutException($"Процесс {expected} не найден за {timeoutSeconds} с. Проверьте имя процесса в настройках. Игра, запущенная лаунчером отдельно, может требовать ручного закрытия.");
        }
        catch
        {
            log.Write($"Tracked process discovery failed: {game.Id}");
            if (!launched.HasExited) await new ManagedProcess(launched, log).CloseAsync(2, CancellationToken.None);
            throw;
        }
        finally { launched.Dispose(); }
    }
}
public sealed class ManagedProcess(Process process, LocalLog log) : IGameProcess
{
    public int Id => process.Id;
    public long StartUtcTicks => process.StartTime.ToUniversalTime().Ticks;
    public nint WindowHandle { get { process.Refresh(); return process.HasExited ? 0 : process.MainWindowHandle; } }
    public bool HasExited => process.HasExited;
    public async Task CloseAsync(int graceSeconds, CancellationToken cancellationToken)
    {
        if (process.HasExited) return;
        log.Write($"Graceful close pid={Id}"); process.CloseMainWindow();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken); timeout.CancelAfter(TimeSpan.FromSeconds(graceSeconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            if (!process.HasExited) { log.Write($"Force terminate process tree pid={Id}"); process.Kill(entireProcessTree: true); await process.WaitForExitAsync(cancellationToken); }
        }
    }
    public void Dispose() => process.Dispose();
}
[SupportedOSPlatform("windows")]
public static class ShortcutService
{
    public static string Create(Game game, string executable)
    {
        Validation.Game(game);
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), $"GameLimiter-Beta-{game.Id}.lnk");
        var type = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows Script Host недоступен.");
        dynamic shell = Activator.CreateInstance(type)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            try { shortcut.TargetPath = executable; shortcut.Arguments = $"--game {game.Id}"; shortcut.WorkingDirectory = Path.GetDirectoryName(executable); shortcut.Description = game.DisplayName; shortcut.IconLocation = (File.Exists(game.IconPath) ? game.IconPath : game.LaunchExecutable) + ",0"; shortcut.Save(); }
            finally { Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        return path;
    }
}
