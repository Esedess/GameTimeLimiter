using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameLimiter.Infrastructure;

namespace GameLimiter.App;

internal sealed class AddTimeWindow : Window
{
    public AddTimeWindow(Store store, SessionCoordinator coordinator)
    {
        var session = coordinator.SessionId;
        Title = "Добавить время"; Width = 460; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        var name = coordinator.ChildId is long child ? store.Children().FirstOrDefault(c => c.Id == child)?.Name ?? "Ребёнок" : "Взрослый";
        panel.Children.Add(Ui.Text(name, 26));
        var remaining = Ui.Text("", 20); panel.Children.Add(remaining);
        panel.Children.Add(Ui.Text(coordinator.ChildId is not null ? "Прибавка к сегодняшнему времени. Дневной лимит не меняется." : "Продление текущей взрослой сессии."));
        var actions = new WrapPanel(); panel.Children.Add(actions);
        void Add(int minutes) { coordinator.AddTime(session, minutes); Close(); }
        foreach (var minutes in new[] { 5, 10, 30 }) actions.Children.Add(Ui.Button($"+{minutes} минут", () => Add(minutes)));
        var custom = Ui.Field(panel, "Добавить минут (1–1440)", "15");
        panel.Children.Add(Ui.Button("Добавить", () => Add(Ui.Number(custom, 1, 1440))));
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        void Update()
        {
            var available = coordinator.CanAddTime && coordinator.SessionId == session;
            remaining.Text = available ? $"Осталось {Ui.Time(coordinator.Remaining)}" : "Сессию уже нельзя продлить";
            actions.IsEnabled = available; custom.IsEnabled = available;
            ((Button)panel.Children[panel.Children.Count - 1]).IsEnabled = available;
        }
        refresh.Tick += (_, _) => Update(); Loaded += (_, _) => refresh.Start(); Closed += (_, _) => refresh.Stop(); Update();
    }
}
