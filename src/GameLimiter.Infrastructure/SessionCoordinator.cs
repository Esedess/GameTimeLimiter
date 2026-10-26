using System.Diagnostics;
using GameLimiter.Core;
namespace GameLimiter.Infrastructure;

public sealed class SessionCoordinator(Store store, IProcessLauncher launcher, LocalLog log)
{
    public bool Active { get; private set; }
    public long? ChildId { get; private set; }
    public event Action<long, SessionState, nint>? Updated;
    public event Action? UsageChanged;
    public event Action? Started;
    private bool stopRequested;
    private bool allowanceReduced;
    private int adultExtraSeconds;
    private bool adultTimed;
    public long SessionId { get; private set; }
    public long Remaining { get; private set; }
    public SessionState State { get; private set; } = SessionState.Idle;
    public bool CanAddTime => Active && !stopRequested && (ChildId is not null || adultTimed)
        && State is SessionState.Running or SessionState.GraceCountdown;
    public void AddTime(long expectedSession, int minutes)
    {
        if (!CanAddTime || SessionId != expectedSession) throw new InvalidOperationException("Эту сессию уже нельзя продлить. Возможно, игра завершилась или началось её закрытие.");
        if (minutes is < 1 or > 1440) throw new ArgumentException("Введите от 1 до 1440 минут.");
        if (ChildId is long child)
        {
            if (!store.Children().Any(c => c.Id == child && c.Enabled)) throw new InvalidOperationException("Ребёнок отключён или архивирован. Проверьте настройки.");
            store.Adjust(child, DateOnly.FromDateTime(DateTime.Now), minutes * 60);
        }
        else adultExtraSeconds = checked(adultExtraSeconds + minutes * 60);
        log.Write($"Time added session={SessionId}, player={ChildId?.ToString() ?? "Adult"}, minutes={minutes}");
        UsageChanged?.Invoke();
    }
    public void SettingsChanged(bool reduced) { allowanceReduced |= reduced; }
    public void Stop() => stopRequested = true;

    public async Task RecoverAsync()
    {
        foreach (var record in store.Interrupted())
        {
            // Never charge the offline interval. Preserve checkpoints and add one lost interval.
            store.AddUsage(record.Id, record.Checkpoint, record.Reserve, recovering: true);
            if (record.Pid is int pid && record.Start is long start)
            {
                try
                {
                    using var p = Process.GetProcessById(pid);
                    if (p.StartTime.ToUniversalTime().Ticks == start)
                    {
                        log.Write($"Recover interrupted session {record.Id}: close known pid={pid}");
                        await new ManagedProcess(p, log).CloseAsync(store.Settings.CloseGrace, CancellationToken.None);
                    }
                }
                catch (ArgumentException) { }
            }
            store.End(record.Id, "Interrupted; one checkpoint interval reserved"); log.Write($"Recovered session {record.Id}");
        }
    }
    public async Task RunAsync(Game game, long? child, int? adultSeconds)
    {
        if (Active) throw new InvalidOperationException("Игра уже запущена. Сначала завершите текущую сессию.");
        if (store.Interrupted().Count > 0) throw new InvalidOperationException("Осталась незавершённая сессия. Закройте игру и перезапустите GameLimiter для восстановления учёта.");
        if (child is not null && !store.Children().Any(c => c.Id == child && c.Enabled)) throw new InvalidOperationException("Этот ребёнок отключён или архивирован.");
        if (child is not null && store.Allowance(child.Value, DateOnly.FromDateTime(DateTime.Now)).Remaining == 0) throw new InvalidOperationException("На сегодня время закончилось.");
        Active = true; ChildId = child; stopRequested = false; allowanceReduced = false;
        State = SessionState.Starting; adultExtraSeconds = 0; adultTimed = adultSeconds is not null;
        IGameProcess? process = null; long session = 0; var elapsed = new Stopwatch(); var persisted = 0; var accounted = DateTime.Now; var reason = "Completed"; Exception? failure = null;
        void Flush(bool final = false)
        {
            var total = (int)(final ? Math.Ceiling(elapsed.Elapsed.TotalSeconds) : Math.Floor(elapsed.Elapsed.TotalSeconds));
            var delta = total - persisted;
            if (delta <= 0) return;
            // Anchor interval to the local clock; elapsed duration itself is monotonic.
            var now = DateTime.Now;
            if (Math.Abs((now - accounted).TotalSeconds - delta) > 30) accounted = now.AddSeconds(-delta);
            store.AddUsage(session, accounted, delta); accounted = accounted.AddSeconds(delta); persisted = total; UsageChanged?.Invoke();
        }
        try
        {
            log.Write($"Selected player={(child?.ToString() ?? "Adult")}, game={game.Id}");
            session = store.Begin(child, game.Id, DateTime.Now, 15, adultTimed: adultSeconds is not null);
            SessionId = session;
            process = await launcher.LaunchAsync(game, store.Settings.Discovery, CancellationToken.None);
            store.Attach(session, process); accounted = DateTime.Now; elapsed.Start(); log.Write($"Session start {session}");
            Started?.Invoke();
            var timer = new SessionTimer();
            while (!process.HasExited)
            {
                var settings = store.Settings;
                if (elapsed.Elapsed.TotalSeconds - persisted >= settings.Checkpoint) Flush();
                long remaining = long.MaxValue;
                if (child is not null)
                {
                    var allowance = store.Allowance(child.Value, DateOnly.FromDateTime(DateTime.Now));
                    var unpersisted = (long)Math.Floor(elapsed.Elapsed.TotalSeconds) - persisted;
                    var todayPending = Math.Min(unpersisted, (long)(DateTime.Now - DateTime.Today).TotalSeconds);
                    remaining = Math.Max(0, allowance.Remaining - todayPending);
                }
                else if (adultSeconds is int duration) remaining = Math.Max(0, (long)duration + adultExtraSeconds - (long)Math.Floor(elapsed.Elapsed.TotalSeconds));
                var display = timer.Evaluate(remaining, elapsed.Elapsed.TotalSeconds, allowanceReduced, settings.AdjustmentGrace); allowanceReduced = false;
                Remaining = remaining; State = timer.State;
                Updated?.Invoke(display, timer.State, process.WindowHandle);
                if (stopRequested || timer.State == SessionState.Closing)
                {
                    State = SessionState.Closing;
                    reason = stopRequested ? "Parent stop" : "Time expired"; log.Write($"Automatic/session close {session}: {reason}");
                    var close = process.CloseAsync(settings.CloseGrace, CancellationToken.None);
                    while (!close.IsCompleted) { await Task.WhenAny(close, Task.Delay(250)); if (elapsed.Elapsed.TotalSeconds - persisted >= settings.Checkpoint) Flush(); }
                    await close; break;
                }
                await Task.Delay(250);
            }
        }
        catch (Exception ex)
        {
            State = SessionState.Failed;
            failure = ex; reason = "Failed"; log.Write($"Session/checkpoint error: {ex}");
            if (process is not null && !process.HasExited)
            {
                try { await process.CloseAsync(store.Settings.CloseGrace, CancellationToken.None); }
                catch (Exception closeError) { log.Write($"Close failure: {closeError}"); failure = new AggregateException("Не удалось закрыть игру. Закройте её вручную.", ex, closeError); }
            }
        }
        finally
        {
            elapsed.Stop();
            try { if (session != 0) { Flush(true); if (process is null || process.HasExited) store.End(session, reason); log.Write($"Session end {session}, duration={store.Duration(session)}, {reason}"); } }
            catch (Exception ex) { failure = new AggregateException("Ошибка сохранения времени; оставлена запись для восстановления.", failure ?? ex, ex); log.Write($"Final checkpoint failure: {ex}"); }
            process?.Dispose(); Active = false; ChildId = null; State = SessionState.Completed; Updated?.Invoke(0, SessionState.Completed, 0); UsageChanged?.Invoke();
        }
        if (failure is not null) throw new InvalidOperationException("Сессия завершилась с ошибкой: " + failure.Message, failure);
    }
}
