using System.IO;
using System.Windows;
using GameLimiter.Infrastructure;
namespace GameLimiter.App;

public partial class App : Application
{
    private Mutex? instance;
    private bool owns;
    public static string DataFolder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLimiter");
    public static LocalLog Log { get; } = new(Path.Combine(DataFolder, "logs"));
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ThemeManager.Apply("Dark");
        var icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/GameLimiter.ico"));
        FrameworkElement.StyleProperty.OverrideMetadata(typeof(Window), new FrameworkPropertyMetadata(new Style(typeof(Window))
        {
            Setters =
            {
                new Setter(Window.IconProperty, icon),
                new Setter(Window.BackgroundProperty, new DynamicResourceExtension("AppBackground")),
                new Setter(Window.ForegroundProperty, new DynamicResourceExtension("AppText")),
                new Setter(Window.FontFamilyProperty, new System.Windows.Media.FontFamily("Segoe UI")),
                new Setter(Window.FontSizeProperty, 14.0)
            }
        }));
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, _) => ThemeManager.UpdateTitleBar((Window)sender)));
        if (e.Args.Length == 2 && e.Args[0] == "--smoke-test")
        {
            try { await SmokeTest.RunAsync(e.Args[1]); Shutdown(0); }
            catch (Exception ex) { File.WriteAllText(Path.Combine(e.Args[1], "failure.txt"), ex.ToString()); Shutdown(1); }
            return;
        }
        DispatcherUnhandledException += (_, args) => { Log.Write("Unhandled: " + args.Exception); MessageBox.Show("Ошибка: " + args.Exception.Message, "GameLimiter"); args.Handled = true; };
        try
        {
            instance = new Mutex(false, "Local\\GameLimiter-Application");
            try { owns = instance.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
            if (!owns) { MessageBox.Show("GameLimiter уже открыт. Если игра идёт, новая сессия недоступна. Для изменения времени откройте окно GameLimiter на панели задач и нажмите «Настройки».", "GameLimiter"); Shutdown(); return; }
            Log.Write("Application startup; args=" + string.Join(" ", e.Args));
            var store = new Store(Path.Combine(DataFolder, "gametime.db")); store.Settings.Validate();
            ThemeManager.Load(store);
            var coordinator = new SessionCoordinator(store, new ProcessLauncher(Log), Log);
            await coordinator.RecoverAsync();
            if (store.Get("ParentPin") is null)
            {
                var pin = new PinWindow(store, true);
                if (pin.ShowDialog() != true) { Shutdown(); return; }
                new SettingsWindow(store, coordinator).ShowDialog();
            }
            string? id = null;
            if (e.Args.Length > 0)
            {
                if (e.Args.Length != 2 || e.Args[0] != "--game") throw new ArgumentException("Запуск: GameLimiter.exe --game <id>");
                id = e.Args[1];
            }
            var game = id is null ? null : store.Games().FirstOrDefault(g => g.Id == id && g.Enabled);
            if (id is not null && game is null) throw new ArgumentException("Игра из ярлыка не найдена или отключена. Проверьте настройки.");
            var window = new MainWindow(store, coordinator, game); MainWindow = window; window.Show();
        }
        catch (Exception ex) { Log.Write("Startup error: " + ex); MessageBox.Show("Не удалось открыть GameLimiter: " + ex.Message, "GameLimiter"); Shutdown(1); }
    }
    protected override void OnExit(ExitEventArgs e) { if (owns) instance?.ReleaseMutex(); instance?.Dispose(); base.OnExit(e); }
}
