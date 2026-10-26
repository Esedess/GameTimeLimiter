using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLimiter.Core;
using GameLimiter.Infrastructure;
namespace GameLimiter.App;

// Developer smoke check: instantiate the published WPF screens and SQLite native provider.
// Uses an explicitly supplied output folder, never the family's live database.
internal static class SmokeTest
{
    public static async Task RunAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        var store = new Store(Path.Combine(folder, "smoke.db"));
        if (store.Children().Count == 0) { store.SaveChild(new(0, "Миша", 1800)); store.SaveChild(new(0, "Саша", 1800)); }
        var game = new Game("test", "Тестовая игра", Environment.ProcessPath!); store.SaveGame(game);
        var coordinator = new SessionCoordinator(store, new ProcessLauncher(new LocalLog(folder)), new LocalLog(folder));
        foreach (var theme in new[] { "Dark", "Light" })
        {
            ThemeManager.Save(store, theme);
            ThemeManager.Load(store);
            if (ThemeManager.Current != theme || store.Get("Theme") != theme) throw new InvalidOperationException("Theme persistence failed.");
            var suffix = theme == "Dark" ? "" : "-light";
            var settings = new SettingsWindow(store, coordinator);
            var tabs = (TabControl)settings.Content;
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                Render(settings, $"settings-{i}{suffix}.png");
                if (i == 1)
                {
                    var scroll = (ScrollViewer)((TabItem)tabs.Items[i]).Content;
                    scroll.ScrollToBottom();
                    Render(settings, $"settings-games-shortcuts{suffix}.png");
                    scroll.ScrollToTop();
                }
                if (i == 4) Render(settings, $"theme-choice{suffix}.png", 470);
            }
            Render(new PinWindow(store, true), $"first-run{suffix}.png");
            Render(new MainWindow(store, coordinator, game), $"players{suffix}.png");
            var previewProcess = new PreviewProcess();
            var preview = new SessionCoordinator(store, new PreviewLauncher(previewProcess), new LocalLog(folder));
            var run = preview.RunAsync(game, store.Children()[0].Id, null);
            var addWindow = new AddTimeWindow(store, preview);
            Render(addWindow, $"add-time{suffix}.png");
            var before = store.Allowance(store.Children()[0].Id, DateOnly.FromDateTime(DateTime.Now)).Adjustment;
            var quick = ((StackPanel)addWindow.Content).Children.OfType<WrapPanel>().Single();
            quick.Children.OfType<Button>().First().RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.Allowance(store.Children()[0].Id, DateOnly.FromDateTime(DateTime.Now)).Adjustment != before + 300)
                throw new InvalidOperationException("Add time button did not persist allowance.");
            preview.Stop(); await run;
            store.Adjust(store.Children()[0].Id, DateOnly.FromDateTime(DateTime.Now), -300);

            // Exercise the real Settings theme button and verify the saved preference.
            var general = (StackPanel)((ScrollViewer)((TabItem)tabs.Items[4]).Content).Content;
            foreach (var checkbox in general.Children.OfType<CheckBox>())
            {
                checkbox.IsChecked = false;
                checkbox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            }
            if (store.MinimizeOnGameStart || store.ExitOnGameEnd) throw new InvalidOperationException("Window preferences did not save.");
            foreach (var checkbox in general.Children.OfType<CheckBox>())
            {
                checkbox.IsChecked = true;
                checkbox.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
            }
            if (!store.MinimizeOnGameStart || !store.ExitOnGameEnd) throw new InvalidOperationException("Window preferences did not restore.");
            var choices = general.Children.OfType<WrapPanel>().First();
            var opposite = theme == "Dark" ? "Светлая" : "Тёмная";
            choices.Children.OfType<Button>().Single(b => (string)b.Content == opposite).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (store.Get("Theme") == theme) throw new InvalidOperationException("Settings theme button did not save the change.");
        }
        await OverlaySmokeTest.RunAsync(folder);
        File.WriteAllText(Path.Combine(folder, "success.txt"), "Published WPF screens, light/dark themes, theme persistence, SQLite, overlay padding, 1080p/1440p/2160p layout and native overlay Z-order recovery verified.");
        void Render(Window window, string filename, int? cropHeight = null)
        {
            var content = (FrameworkElement)window.Content;
            var width = double.IsNaN(window.Width) ? 600 : window.Width;
            var height = double.IsNaN(window.Height) ? 450 : window.Height;
            content.Measure(new Size(width, height)); content.Arrange(new Rect(0, 0, width, height)); content.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)width, cropHeight ?? (int)height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual(); using (var drawing = background.RenderOpen()) drawing.DrawRectangle((Brush)Application.Current.Resources["AppBackground"], null, new Rect(0, 0, width, height));
            bitmap.Render(background); bitmap.Render(content);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap)); using var output = File.Create(Path.Combine(folder, filename)); encoder.Save(output);
        }
    }
    private sealed class PreviewLauncher(PreviewProcess process) : IProcessLauncher
    {
        public Task<IGameProcess> LaunchAsync(Game game, int timeoutSeconds, CancellationToken cancellationToken) => Task.FromResult<IGameProcess>(process);
    }
    private sealed class PreviewProcess : IGameProcess
    {
        public int Id => 0;
        public long StartUtcTicks => 0;
        public nint WindowHandle => 0;
        public bool HasExited { get; private set; }
        public Task CloseAsync(int graceSeconds, CancellationToken cancellationToken) { HasExited = true; return Task.CompletedTask; }
        public void Dispose() { }
    }
}
