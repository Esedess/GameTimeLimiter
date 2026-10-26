using System.Text.RegularExpressions;
namespace GameLimiter.Core;

public sealed record Child(long Id, string Name, int BaseSeconds, bool Enabled = true, bool Archived = false);
public sealed record Game(string Id, string DisplayName, string LaunchExecutable, string LaunchArguments = "", string WorkingDirectory = "", string TrackedProcessName = "", bool Enabled = true, string IconPath = "");
public sealed record TimerSettings(int First = 300, int Urgent = 120, int Continuous = 60, int Large = 10, int AdjustmentGrace = 60, int CloseGrace = 5, int Checkpoint = 5, int Discovery = 60)
{
    public void Validate()
    {
        if (!(First >= Urgent && Urgent >= Continuous && Continuous >= Large && Large >= 1 && First <= 86400) || AdjustmentGrace is < 1 or > 600 || CloseGrace is < 1 or > 60 || Checkpoint is < 1 or > 15 || Discovery is < 1 or > 300)
            throw new ArgumentException("Проверьте пороги предупреждений и интервалы: пороги должны убывать; запись 1–15 с, отсрочка 1–600 с, закрытие 1–60 с, поиск 1–300 с.");
    }
}
public static class Validation
{
    public static void Child(Child child)
    {
        if (string.IsNullOrWhiteSpace(child.Name) || child.Name.Length > 80 || child.BaseSeconds is < 0 or > 86400) throw new ArgumentException("Укажите имя и дневной лимит от 0 до 1440 минут.");
    }
    public static void Game(Game game)
    {
        if (!Regex.IsMatch(game.Id, "^[a-zA-Z0-9_-]{1,64}$") || string.IsNullOrWhiteSpace(game.DisplayName)) throw new ArgumentException("Укажите название и Id: латинские буквы, цифры, дефис, подчёркивание.");
        if (!Path.IsPathFullyQualified(game.LaunchExecutable) || !File.Exists(game.LaunchExecutable)) throw new ArgumentException("Исполняемый файл не найден. Выберите полный путь к exe.");
        if (game.WorkingDirectory.Length > 0 && !Directory.Exists(game.WorkingDirectory)) throw new ArgumentException("Рабочий каталог не найден.");
        if (game.TrackedProcessName.IndexOfAny(['/', '\\', '"']) >= 0) throw new ArgumentException("Укажите имя процесса, например game.exe, без пути.");
    }
}
public sealed record Allowance(long Base, long Adjustment, long Used)
{
    public long Effective => Math.Max(0, Base + Adjustment);
    public long Remaining => Math.Max(0, Effective - Used);
}
public enum SessionState { Idle, Starting, Running, GraceCountdown, Closing, Completed, Failed }
public sealed class SessionTimer
{
    private double? graceEnd;
    public SessionState State { get; private set; } = SessionState.Running;
    public long Evaluate(long remaining, double elapsed, bool allowanceReduced, int grace)
    {
        if (remaining > 0) { graceEnd = null; State = SessionState.Running; return remaining; }
        if (allowanceReduced && graceEnd is null) graceEnd = elapsed + grace;
        if (graceEnd is not null && elapsed < graceEnd) { State = SessionState.GraceCountdown; return (long)Math.Ceiling(graceEnd.Value - elapsed); }
        State = SessionState.Closing; return 0;
    }
}
public interface IGameProcess : IDisposable
{
    int Id { get; }
    long StartUtcTicks { get; }
    nint WindowHandle { get; }
    bool HasExited { get; }
    Task CloseAsync(int graceSeconds, CancellationToken cancellationToken);
}
public interface IProcessLauncher
{
    Task<IGameProcess> LaunchAsync(Game game, int timeoutSeconds, CancellationToken cancellationToken);
}
public interface IChildProcessLauncher : IProcessLauncher
{
    Task<IGameProcess> LaunchForChildAsync(Game game, long? child, int timeoutSeconds, CancellationToken cancellationToken);
}
