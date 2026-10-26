using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GameLimiter.Core;
using GameLimiter.Infrastructure;
using Microsoft.Win32;
namespace GameLimiter.App;

public sealed class SettingsWindow : Window
{
    private readonly Store store;
    private readonly SessionCoordinator coordinator;
    private readonly ListBox children = new() { DisplayMemberPath = "Name", MinHeight = 100, MaxHeight = 170 };
    private readonly ListBox games = new() { DisplayMemberPath = "DisplayName", MinHeight = 100, MaxHeight = 150 };
    private readonly TextBlock stats = Ui.Text("");
    private readonly TextBox childName, childLimit, gameId, gameName, executable, arguments, working, tracked, icon;
    private readonly CheckBox childEnabled, gameEnabled;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private Child? selectedChild;
    private Game? selectedGame;
    public SettingsWindow(Store store, SessionCoordinator coordinator)
    {
        this.store = store; this.coordinator = coordinator;
        Title = "GameLimiter Beta — настройки родителя"; Width = 780; Height = 820; MinWidth = 620; MinHeight = 520; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var tabs = new TabControl { Margin = new Thickness(12) }; Content = tabs;
        StackPanel Tab(string name) { var panel = new StackPanel { Margin = new Thickness(16) }; tabs.Items.Add(new TabItem { Header = name, Content = new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } }); return panel; }
        var childPanel = Tab("Дети"); childPanel.Children.Add(children);
        childName = Ui.Field(childPanel, "Имя"); childLimit = Ui.Field(childPanel, "Базовый лимит, минут", "30"); childEnabled = Ui.Check(childPanel, "Включён", true);
        var childActions = new WrapPanel(); childPanel.Children.Add(childActions);
        childActions.Children.Add(Ui.Button("Добавить ребёнка", () => { selectedChild = null; children.SelectedItem = null; childName.Text = ""; childLimit.Text = "30"; childEnabled.IsChecked = true; stats.Text = "Введите имя и сохраните ребёнка."; childName.Focus(); }));
        childActions.Children.Add(Ui.Button("Сохранить", () =>
        {
            var old = selectedChild;
            selectedChild = store.SaveChild(new(old?.Id ?? 0, childName.Text.Trim(), Ui.Number(childLimit, 0, 1440) * 60, childEnabled.IsChecked == true));
            coordinator.SettingsChanged(old is not null && coordinator.ChildId == old.Id && (selectedChild.BaseSeconds < old.BaseSeconds || !selectedChild.Enabled));
            App.Log.Write($"Child saved {selectedChild.Id}"); ReloadChildren(selectedChild.Id);
        }));
        childActions.Children.Add(Ui.Button("Архивировать", () =>
        {
            var child = RequireChild(); store.SaveChild(child with { Archived = true, Enabled = false }); coordinator.SettingsChanged(coordinator.ChildId == child.Id); App.Log.Write($"Child archived {child.Id}"); ReloadChildren();
        }));
        childPanel.Children.Add(stats);
        var plusMinus = new WrapPanel(); childPanel.Children.Add(plusMinus);
        foreach (var minutes in new[] { 5, 10, 15, 30, -5, -10, -15, -30 }) plusMinus.Children.Add(Ui.Button($"{minutes:+0;-0} мин", () => Adjust(minutes)));
        var custom = Ui.Field(childPanel, "Своя поправка: добавить/вычесть минут сегодня (например −12)", "0"); childPanel.Children.Add(Ui.Button("Применить поправку", () => Adjust(Ui.Number(custom, -1440, 1440))));
        childPanel.Children.Add(Ui.Text("Поправки складываются и действуют только сегодня. При исчерпании времени у играющего ребёнка сначала появляется отсрочка закрытия."));
        children.SelectionChanged += (_, _) => { if (children.SelectedItem is Child child) { selectedChild = child; childName.Text = child.Name; childLimit.Text = (child.BaseSeconds / 60).ToString(); childEnabled.IsChecked = child.Enabled; UpdateStats(); } };

        var gamePanel = Tab("Игры"); gamePanel.Children.Add(games);
        gameId = Ui.Field(gamePanel, "Id для ярлыка (латиница, цифры, - и _)"); gameName = Ui.Field(gamePanel, "Название"); gameEnabled = Ui.Check(gamePanel, "Включена", true);
        executable = Ui.Field(gamePanel, "Запускаемый exe (полный путь)"); gamePanel.Children.Add(Ui.Button("Выбрать exe…", () => BrowseFile(executable, "Программы|*.exe|Все файлы|*.*")));
        arguments = Ui.Field(gamePanel, "Аргументы запуска"); working = Ui.Field(gamePanel, "Рабочий каталог (пусто = каталог exe)");
        gamePanel.Children.Add(Ui.Button("Выбрать каталог…", () => { var dialog = new OpenFolderDialog(); if (dialog.ShowDialog(this) == true) working.Text = dialog.FolderName; }));
        tracked = Ui.Field(gamePanel, "Имя игрового процесса, например game.exe (пусто = запускаемый exe)"); icon = Ui.Field(gamePanel, "Значок: exe или ico (пусто = значок игры)"); gamePanel.Children.Add(Ui.Button("Выбрать значок…", () => BrowseFile(icon, "Значки и программы|*.ico;*.exe|Все файлы|*.*")));
        var gameActions = new WrapPanel(); gamePanel.Children.Add(gameActions);
        gameActions.Children.Add(Ui.Button("Добавить игру", () => { selectedGame = null; games.SelectedItem = null; gameId.IsEnabled = true; foreach (var field in new[] { gameId, gameName, executable, arguments, working, tracked, icon }) field.Text = ""; gameEnabled.IsChecked = true; gameId.Focus(); }));
        gameActions.Children.Add(Ui.Button("Сохранить", () => SaveGame()));
        gameActions.Children.Add(Ui.Button("Удалить игру", () => { if (selectedGame is null) return; store.RemoveGame(selectedGame.Id); App.Log.Write("Game removed " + selectedGame.Id); ReloadGames(); }));
        gameActions.Children.Add(Ui.Button("Создать / обновить ярлык", () =>
        {
            var game = SaveGame(); var path = ShortcutService.Create(game, Path.Combine(AppContext.BaseDirectory, "GameLimiter.exe")); App.Log.Write("Shortcut created " + game.Id); MessageBox.Show("Ярлык сохранён:\n" + path, "GameLimiter");
        }));
        games.SelectionChanged += (_, _) =>
        {
            if (games.SelectedItem is not Game game) return;
            selectedGame = game; gameId.Text = game.Id; gameId.IsEnabled = false; gameName.Text = game.DisplayName; gameEnabled.IsChecked = game.Enabled; executable.Text = game.LaunchExecutable; arguments.Text = game.LaunchArguments; working.Text = game.WorkingDirectory; tracked.Text = game.TrackedProcessName; icon.Text = game.IconPath;
        };
        var warningPanel = Tab("Таймер"); var settings = store.Settings;
        var first = Ui.Field(warningPanel, "Первое предупреждение в углу, секунд", settings.First.ToString()); var urgent = Ui.Field(warningPanel, "Заметное предупреждение, секунд", settings.Urgent.ToString()); var continuous = Ui.Field(warningPanel, "Крупный красно-жёлтый отсчёт в углу, секунд", settings.Continuous.ToString()); var large = Ui.Field(warningPanel, "Самый крупный отсчёт в углу, секунд", settings.Large.ToString());
        var adjustmentGrace = Ui.Field(warningPanel, "Отсрочка после уменьшения лимита, секунд", settings.AdjustmentGrace.ToString()); var closeGrace = Ui.Field(warningPanel, "Ожидание мягкого закрытия, секунд", settings.CloseGrace.ToString()); var checkpoint = Ui.Field(warningPanel, "Сохранение времени каждые N секунд", settings.Checkpoint.ToString()); var discovery = Ui.Field(warningPanel, "Ожидание игрового процесса, секунд", settings.Discovery.ToString());
        warningPanel.Children.Add(Ui.Button("Сохранить таймер", () => { store.SaveSettings(new(Ui.Number(first, 1, 86400), Ui.Number(urgent, 1, 86400), Ui.Number(continuous, 1, 86400), Ui.Number(large, 1, 86400), Ui.Number(adjustmentGrace, 1, 600), Ui.Number(closeGrace, 1, 60), Ui.Number(checkpoint, 1, 15), Ui.Number(discovery, 1, 300))); App.Log.Write("Timer settings saved"); MessageBox.Show("Настройки таймера сохранены.", "GameLimiter"); }));
        var pinPanel = Tab("PIN"); pinPanel.Children.Add(Ui.Text("Новый PIN (4–12 цифр)")); var newPin = new PasswordBox { MaxLength = 12, FontSize = 20 }; pinPanel.Children.Add(newPin); pinPanel.Children.Add(Ui.Text("Повторите PIN")); var confirmPin = new PasswordBox { MaxLength = 12, FontSize = 20 }; pinPanel.Children.Add(confirmPin);
        pinPanel.Children.Add(Ui.Button("Изменить PIN", () => { if (newPin.Password != confirmPin.Password) throw new ArgumentException("PIN не совпадают."); store.Set("ParentPin", PinService.Hash(newPin.Password)); newPin.Clear(); confirmPin.Clear(); App.Log.Write("Parent PIN changed"); MessageBox.Show("PIN изменён.", "GameLimiter"); }));
        var diagnostics = Tab("Общие");
        diagnostics.Children.Add(Ui.Text("Поведение приложения", 20));
        var minimize = Ui.Check(diagnostics, "Сворачивать GameLimiter при запуске игры", store.MinimizeOnGameStart);
        var exit = Ui.Check(diagnostics, "Закрывать GameLimiter после завершения игры", store.ExitOnGameEnd);
        minimize.Click += (_, _) => Ui.Guard(() => { store.MinimizeOnGameStart = minimize.IsChecked == true; App.Log.Write("Minimize on game start: " + store.MinimizeOnGameStart); });
        exit.Click += (_, _) => Ui.Guard(() => { store.ExitOnGameEnd = exit.IsChecked == true; App.Log.Write("Exit on game end: " + store.ExitOnGameEnd); });
        diagnostics.Children.Add(Ui.Text("Оба параметра включены по умолчанию. Изменения сохраняются сразу."));
        diagnostics.Children.Add(Ui.Text("Оформление", 20));
        var themeStatus = Ui.Text(ThemeManager.Current == "Dark" ? "Текущая тема: тёмная" : "Текущая тема: светлая");
        var themeButtons = new WrapPanel();
        foreach (var (label, value) in new[] { ("Светлая", "Light"), ("Тёмная", "Dark") })
        {
            themeButtons.Children.Add(Ui.Button(label, () =>
            {
                ThemeManager.Save(store, value);
                themeStatus.Text = "Текущая тема: " + (value == "Dark" ? "тёмная" : "светлая");
                App.Log.Write("Theme changed: " + value);
            }));
        }
        diagnostics.Children.Add(themeButtons);
        diagnostics.Children.Add(themeStatus);
        diagnostics.Children.Add(Ui.Text("Выбор сохраняется сразу и применяется ко всем окнам."));
        diagnostics.Children.Add(Ui.Text("Диагностика", 20));
        diagnostics.Children.Add(Ui.Text("GameLimiter Beta 1.1.0-beta.1 · Windows x64\nБаза: " + Path.Combine(App.DataFolder, "gametime.db") + "\nЖурналы: " + Path.Combine(App.DataFolder, "logs")));
        diagnostics.Children.Add(Ui.Button("Открыть папку данных", () => Process.Start(new ProcessStartInfo(App.DataFolder) { UseShellExecute = true })));
        diagnostics.Children.Add(Ui.Button("Открыть журналы", () => { var folder = Path.Combine(App.DataFolder, "logs"); Directory.CreateDirectory(folder); Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true }); }));
        diagnostics.Children.Add(Ui.Text("Обычный запуск игры — через её ярлык. Во время игры окно GameLimiter можно свернуть; настройки доступны на панели задач. Изменения полей сохраняются кнопкой «Сохранить»."));
        var profilePanel = Tab("Профиль Windows");
        profilePanel.Children.Add(Ui.Text("Запуск игр ребёнка от другого пользователя", 20));
        profilePanel.Children.Add(Ui.Text("Beta: локальные пользователи с паролем. Для контроля их игр запустите GameLimiter Beta от имени администратора. Сохранения зависят от самой игры и облачного аккаунта."));
        profilePanel.Children.Add(Ui.Text("Ребёнок"));
        var profileChild = new ListBox { DisplayMemberPath = "Name", Height = 75, Margin = new Thickness(0, 8, 0, 8) }; profilePanel.Children.Add(profileChild);
        var useCurrent = Ui.Check(profilePanel, "Использовать текущего пользователя (по умолчанию)", true);
        profilePanel.Children.Add(Ui.Text("Пользователь Windows (можно выбрать или ввести ИМЯ-ПК\\имя)"));
        var accounts = new ListBox { Height = 75 }; profilePanel.Children.Add(accounts);
        var account = new TextBox { MinHeight = 32 }; profilePanel.Children.Add(account);
        accounts.SelectionChanged += (_, _) => { if (accounts.SelectedItem is string value) account.Text = value; };
        try { accounts.ItemsSource = WindowsProfiles.LocalAccounts(); } catch (Exception ex) { App.Log.Write("Local account listing failed: " + ex.Message); }
        profilePanel.Children.Add(Ui.Text("Пароль Windows — не PIN Windows Hello"));
        var password = new PasswordBox { FontSize = 20 }; profilePanel.Children.Add(password);
        var profileStatus = Ui.Text(""); profilePanel.Children.Add(profileStatus);
        void LoadProfile()
        {
            password.Clear();
            var saved = profileChild.SelectedItem is Child c ? WindowsProfiles.Get(store, c.Id) : null;
            useCurrent.IsChecked = saved is null; account.Text = saved is null ? "" : saved.Domain + "\\" + saved.User;
            accounts.IsEnabled = account.IsEnabled = password.IsEnabled = saved is not null;
            profileStatus.Text = saved is null ? "Запуск от текущего пользователя." : "Пароль сохранён защищённо. Для изменения введите его заново.";
        }
        profileChild.SelectionChanged += (_, _) => Ui.Guard(LoadProfile);
        useCurrent.Click += (_, _) => { accounts.IsEnabled = account.IsEnabled = password.IsEnabled = useCurrent.IsChecked != true; profileStatus.Text = "Изменения ещё не сохранены."; };
        profilePanel.Children.Add(Ui.Button("Проверить вход и сохранить", () =>
        {
            if (profileChild.SelectedItem is not Child child) throw new ArgumentException("Выберите ребёнка. Нового ребёнка сначала сохраните на вкладке «Дети».");
            try
            {
                if (useCurrent.IsChecked == true) WindowsProfiles.Clear(store, child.Id);
                else { using var secret = password.SecurePassword; WindowsProfiles.Save(store, child.Id, account.Text, secret); }
                App.Log.Write("Windows launch profile updated for child=" + child.Id); LoadProfile();
                profileStatus.Text = "Сохранено. Настройка применяется при следующем запуске игры.";
            }
            finally { password.Clear(); }
        }));
        profilePanel.Children.Add(Ui.Text("Выбор текущего пользователя и сохранение удаляют сохранённые учётные данные. Пароль защищён DPAPI текущего пользователя Windows; после переноса базы на другой ПК или в другой профиль его нужно ввести заново. Команды игр и аргументы ярлыков не меняются. Взрослые сессии запускаются от текущего пользователя."));
        tabs.SelectionChanged += (_, e) =>
        {
            if (e.Source == tabs && tabs.SelectedIndex == 5) { var id = (profileChild.SelectedItem as Child)?.Id; profileChild.ItemsSource = store.Children(); profileChild.SelectedItem = profileChild.Items.Cast<Child>().FirstOrDefault(c => c.Id == id) ?? profileChild.Items.Cast<Child>().FirstOrDefault(); }
        };
        profileChild.ItemsSource = store.Children(); profileChild.SelectedIndex = 0;
        ReloadChildren(); ReloadGames(); timer.Tick += (_, _) => Ui.Guard(UpdateStats); timer.Start(); Closed += (_, _) => timer.Stop();
    }
    private Child RequireChild() => selectedChild ?? throw new ArgumentException("Сначала выберите или сохраните ребёнка.");
    private void Adjust(int minutes) { var child = RequireChild(); store.Adjust(child.Id, DateOnly.FromDateTime(DateTime.Now), minutes * 60); coordinator.SettingsChanged(minutes < 0 && coordinator.ChildId == child.Id); App.Log.Write($"Allowance adjustment child={child.Id}, seconds={minutes * 60}"); UpdateStats(); }
    private void UpdateStats() { if (selectedChild is null) return; var a = store.Allowance(selectedChild.Id, DateOnly.FromDateTime(DateTime.Now)); stats.Text = $"Сегодня {DateTime.Today:d}\nБазовый лимит: {Ui.Time(selectedChild.BaseSeconds)}   Поправка: {Ui.Time(a.Adjustment)}\nИспользовано: {Ui.Time(a.Used)}   Осталось: {Ui.Time(a.Remaining)}\nВремя обновляется при сохранении контрольной записи."; }
    private void ReloadChildren(long? id = null) { children.ItemsSource = store.Children(); children.SelectedItem = children.Items.Cast<Child>().FirstOrDefault(c => c.Id == id) ?? children.Items.Cast<Child>().FirstOrDefault(); if (children.SelectedItem is null) { selectedChild = null; stats.Text = "Добавьте ребёнка."; } }
    private void ReloadGames(string? id = null) { games.ItemsSource = store.Games(); games.SelectedItem = games.Items.Cast<Game>().FirstOrDefault(g => g.Id == id) ?? games.Items.Cast<Game>().FirstOrDefault(); if (games.SelectedItem is null) { selectedGame = null; gameId.IsEnabled = true; } }
    private Game SaveGame()
    {
        var game = new Game(gameId.Text.Trim(), gameName.Text.Trim(), executable.Text.Trim(), arguments.Text, working.Text.Trim(), tracked.Text.Trim(), gameEnabled.IsChecked == true, icon.Text.Trim());
        if (selectedGame is null && store.Games().Any(g => g.Id == game.Id)) throw new ArgumentException("Этот Id уже существует. Выберите игру из списка для изменения.");
        store.SaveGame(game); App.Log.Write("Game saved " + game.Id); ReloadGames(game.Id); return game;
    }
    private void BrowseFile(TextBox field, string filter) { var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true }; if (dialog.ShowDialog(this) == true) field.Text = dialog.FileName; }
}
