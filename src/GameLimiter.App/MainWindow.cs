using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameLimiter.Core;
using GameLimiter.Infrastructure;
namespace GameLimiter.App;

public sealed class MainWindow : Window
{
    private readonly Store store;
    private readonly SessionCoordinator coordinator;
    private readonly Game? game;
    private readonly StackPanel players = new();
    private readonly TextBlock status = Ui.Text("", 24);
    private readonly OverlayWindow overlay = new();
    private readonly DispatcherTimer refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    public MainWindow(Store store, SessionCoordinator coordinator, Game? game)
    {
        this.store = store; this.coordinator = coordinator; this.game = game;
        Title = "GameLimiter"; Width = 520; Height = 620; MinWidth = 420; MinHeight = 360; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        panel.Children.Add(Ui.Text(game?.DisplayName ?? "GameLimiter", 30)); panel.Children.Add(status); panel.Children.Add(players);
        panel.Children.Add(Ui.Button("Настройки родителя", () => { if (Authenticate()) new SettingsWindow(store, coordinator) { Owner = this }.ShowDialog(); }));
        panel.Children.Add(Ui.Button("Завершить текущую игру", () => { if (coordinator.Active && Authenticate()) coordinator.Stop(); }));
        coordinator.Updated += (remaining, state, handle) => { status.Text = state == SessionState.Completed ? "Игра завершена" : remaining == long.MaxValue ? "Взрослый · без лимита" : $"Осталось {Ui.Time(remaining)}"; overlay.Update(remaining, state, handle, store.Settings); };
        refresh.Tick += (_, _) => { if (!coordinator.Active) RefreshPlayers(); }; refresh.Start(); RefreshPlayers();
        Closing += (_, e) => { if (coordinator.Active) { e.Cancel = true; MessageBox.Show("Сначала завершите игру. Таймер продолжает работать; окно можно свернуть.", "GameLimiter"); } };
        Closed += (_, _) => { refresh.Stop(); overlay.Close(); Application.Current.Shutdown(); };
    }
    private bool Authenticate() => new PinWindow(store) { Owner = this }.ShowDialog() == true;
    private void RefreshPlayers()
    {
        players.Children.Clear();
        if (game is null) { status.Text = "Настройте детей и игры"; players.Children.Add(Ui.Text("Игры запускаются через созданные в настройках ярлыки на рабочем столе.")); return; }
        status.Text = "Кто играет?";
        foreach (var child in store.Children().Where(c => c.Enabled))
        {
            var remaining = store.Allowance(child.Id, DateOnly.FromDateTime(DateTime.Now)).Remaining;
            var button = Ui.Button(child.Name + "\n" + (remaining == 0 ? "На сегодня время закончилось" : Ui.Time(remaining)), () => _ = StartAsync(child.Id, null)); button.FontSize = 24; button.MinHeight = 80; button.IsEnabled = remaining > 0; players.Children.Add(button);
        }
        var adult = Ui.Button("Взрослый", () => { if (Authenticate()) AdultChoices(); }); adult.FontSize = 24; players.Children.Add(adult);
    }
    private void AdultChoices()
    {
        var dialog = new Window { Title = "Взрослая сессия", Width = 360, SizeToContent = SizeToContent.Height, Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        var panel = new StackPanel { Margin = new Thickness(20) }; dialog.Content = panel;
        foreach (var minutes in new[] { 15, 30, 60 }) panel.Children.Add(Ui.Button($"{minutes} минут", () => { dialog.Close(); _ = StartAsync(null, minutes * 60); }));
        var custom = Ui.Field(panel, "Свой лимит, минут", "45");
        panel.Children.Add(Ui.Button("Запустить с этим лимитом", () => { var seconds = Ui.Number(custom, 1, 1440) * 60; dialog.Close(); _ = StartAsync(null, seconds); }));
        panel.Children.Add(Ui.Button("Без лимита", () => { dialog.Close(); _ = StartAsync(null, null); }));
        panel.Children.Add(Ui.Button("Настройки", () => new SettingsWindow(store, coordinator) { Owner = dialog }.ShowDialog())); dialog.ShowDialog();
    }
    private async Task StartAsync(long? child, int? adultSeconds)
    {
        players.IsEnabled = false; status.Text = "Запускаем игру…";
        try
        {
            var currentGame = store.Games().FirstOrDefault(g => g.Id == game!.Id && g.Enabled) ?? throw new InvalidOperationException("Игра удалена или отключена в настройках.");
            await coordinator.RunAsync(currentGame, child, adultSeconds);
        }
        catch (Exception ex) { App.Log.Write("Launch/session failure: " + ex); MessageBox.Show(ex.Message, "GameLimiter"); }
        finally { players.IsEnabled = true; RefreshPlayers(); }
    }
}
