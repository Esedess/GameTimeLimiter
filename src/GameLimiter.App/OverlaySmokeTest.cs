using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GameLimiter.Core;
namespace GameLimiter.App;

internal static class OverlaySmokeTest
{
    public static async Task RunAsync(string folder)
    {
        var report = new List<string>();
        var overlay = new OverlayWindow { Opacity = 0 }; // Native checks never cover the user's screen.
        try
        {
            foreach (var sample in new[] { (300L, 0L, "5min"), (60L, 0L, "1min-red"),
                (60L, 500L, "1min-yellow"), (10L, 0L, "10sec"), (0L, 0L, "expired") })
            {
                var state = sample.Item1 == 0 ? SessionState.Closing : SessionState.Running;
                var size = overlay.RenderContent(sample.Item1, state, new(), sample.Item2);
                var border = (Border)overlay.Content;
                var text = (TextBlock)border.Child;
                var glyphs = new FormattedText(text.Text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                    new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch), text.FontSize, text.Foreground, 1);
                if (size.Width + 1 < glyphs.WidthIncludingTrailingWhitespace + 30 || size.Height + 1 < glyphs.Height + 20)
                    throw new InvalidOperationException("Overlay clips the glyphs after a font-size transition.");
                if (Math.Abs(size.Width - text.DesiredSize.Width - 30) > 1 || Math.Abs(size.Height - text.DesiredSize.Height - 20) > 1)
                    throw new InvalidOperationException("Overlay padding is not 15 horizontal / 10 vertical.");
                border.Arrange(new Rect(new Point(), size)); border.UpdateLayout();
                Save(border, size, 1, Path.Combine(folder, $"overlay-{sample.Item3}.png"));
                foreach (var display in new[] { (1920, 1080), (2560, 1440), (3840, 2160) })
                foreach (var scale in new[] { 1.0, 1.25, 1.5, 2.0 })
                {
                    var appearance = OverlayAppearance.For(sample.Item1, state, new(), sample.Item2);
                    var placement = OverlayLayout.Place(new(0, 0, display.Item1, display.Item2), scale, size.Width, size.Height, appearance.Centered);
                    if (placement.X < 0 || placement.Y < 0 || placement.X + placement.Width > display.Item1 || placement.Y + placement.Height > display.Item2)
                        throw new InvalidOperationException("Overlay clipped at display edge.");
                    report.Add($"{sample.Item3}: {display.Item1}x{display.Item2} / {scale:P0}, bounds={placement}");
                    if (!(scale == 1 && display.Item1 is 1920 or 2560) &&
                        !(scale == 1.25 && display.Item1 == 2560 && sample.Item3 == "1min-red")) continue;
                    var visual = new DrawingVisual();
                    using (var drawing = visual.RenderOpen())
                    {
                        drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(12, 19, 31)), null, new Rect(0, 0, display.Item1, display.Item2));
                        var label = new FormattedText($"GameLimiter overlay layout test\n{display.Item1} × {display.Item2} · {scale:P0}\n{sample.Item3}", CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, new Typeface("Segoe UI"), 24, Brushes.SlateGray, 1);
                        drawing.DrawText(label, new Point(40, display.Item2 - 140));
                        drawing.DrawRectangle(new VisualBrush(border) { Stretch = Stretch.Fill }, null,
                            new Rect(placement.X, placement.Y, placement.Width, placement.Height));
                    }
                    // Screens are actual WPF overlay content on a neutral test surface, not screenshots of a game.
                    Save(visual, new Size(display.Item1, display.Item2), 1,
                        Path.Combine(folder, $"overlay-layout-{display.Item1}x{display.Item2}-{(int)(scale * 100)}-{sample.Item3}.png"));
                }
            }

            var game = new Window { Width = 80, Height = 80, WindowStyle = WindowStyle.None, AllowsTransparency = true,
                Background = Brushes.Transparent, Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
            try
            {
                game.Show();
                var gameHandle = new WindowInteropHelper(game).Handle;
                overlay.Update(60, SessionState.Running, gameHandle, new());
                var overlayHandle = new WindowInteropHelper(overlay).Handle;
                void CheckNoActivation()
                {
                    var foreground = GetForegroundWindow();
                    // The user may legitimately Alt+Tab while this diagnostic runs. Only these
                    // no-activate test windows must never become foreground, not the whole desktop.
                    if (foreground == overlayHandle || foreground == gameHandle)
                        throw new InvalidOperationException("A no-activate test window stole foreground focus.");
                }
                CheckNoActivation();
                if (!OverlayWindow.IsAbove(overlayHandle, gameHandle)) throw new InvalidOperationException("Overlay initially below test game.");
                for (var attempt = 0; attempt < 3; attempt++)
                {
                    OverlayWindow.RaiseForSmokeTest(gameHandle);
                    if (!OverlayWindow.IsAbove(gameHandle, overlayHandle)) throw new InvalidOperationException("Test did not reproduce game rising over overlay.");
                    await Task.Delay(600);
                    if (!OverlayWindow.IsAbove(overlayHandle, gameHandle)) throw new InvalidOperationException("Overlay did not recover Z-order without a main-window click.");
                    CheckNoActivation();
                }
                overlay.Update(0, SessionState.Closing, gameHandle, new());
                OverlayWindow.RaiseForSmokeTest(gameHandle);
                await Task.Delay(600);
                if (!OverlayWindow.IsAbove(overlayHandle, gameHandle)) throw new InvalidOperationException("Expiry overlay lost Z-order during closing.");
                CheckNoActivation();
                var styles = GetWindowLongPtr(overlayHandle, -20).ToInt64();
                if ((styles & (0x20 | 0x80 | 0x08000000)) != (0x20 | 0x80 | 0x08000000)) throw new InvalidOperationException("Missing transparent/toolwindow/noactivate styles.");
                overlay.Update(0, SessionState.Completed, 0, new());
                if (overlay.IsVisible) throw new InvalidOperationException("Completed overlay remained visible.");
                report.Add("Native HWND: 3 game Z-order raises recovered; expiry maintained; test windows never became foreground; click-through/toolwindow/noactivate styles verified.");
            }
            finally { game.Close(); }
        }
        finally { overlay.Close(); }
        File.WriteAllLines(Path.Combine(folder, "overlay-verification.txt"), report);
    }

    private static void Save(Visual visual, Size size, double scale, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width * scale), (int)Math.Ceiling(size.Height * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path); encoder.Save(file);
    }
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint window, int index);
}
