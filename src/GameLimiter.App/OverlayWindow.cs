using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using GameLimiter.Core;
namespace GameLimiter.App;

public sealed class OverlayWindow : Window
{
    private readonly TextBlock text = new()
    {
        Foreground = Brushes.White,
        TextAlignment = TextAlignment.Center,
        TextWrapping = TextWrapping.Wrap,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
        FontFamily = new FontFamily("Segoe UI"),
        Margin = new Thickness(0)
    };
    private readonly Border frame;
    private readonly DispatcherTimer refresh = new(DispatcherPriority.Normal) { Interval = TimeSpan.FromMilliseconds(250) };
    private readonly Stopwatch animation = Stopwatch.StartNew();
    private long seconds;
    private SessionState state;
    private TimerSettings settings = new();
    private nint gameWindow;
    private nint lastMonitor;
    private bool positioning;
    private bool nativeFailureLogged;
    private static readonly nint HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;

    public OverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowActivated = false;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        IsHitTestVisible = false;
        Focusable = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        UseLayoutRounding = true;
        frame = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 27, 33, 43)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(OverlayLayout.HorizontalPadding, OverlayLayout.VerticalPadding,
                OverlayLayout.HorizontalPadding, OverlayLayout.VerticalPadding),
            Child = text
        };
        Content = frame;
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLongPtr(hwnd, -20, new nint(GetWindowLongPtr(hwnd, -20).ToInt64() | 0x20 | 0x80 | 0x08000000));
            HwndSource.FromHwnd(hwnd)?.AddHook(WindowHook);
        };
        refresh.Tick += (_, _) => RefreshNativeWindow();
        Closed += (_, _) => refresh.Stop();
    }

    public void Update(long remaining, SessionState sessionState, nint trackedWindow, TimerSettings timerSettings)
    {
        seconds = remaining;
        state = sessionState;
        settings = timerSettings;
        gameWindow = trackedWindow;
        if (!OverlayAppearance.For(seconds, state, settings, animation.ElapsedMilliseconds).Visible)
        {
            refresh.Stop();
            Hide();
            return;
        }
        RenderContent(seconds, state, settings, animation.ElapsedMilliseconds);
        Width = frame.DesiredSize.Width;
        Height = frame.DesiredSize.Height;
        if (!IsVisible) Show();
        RefreshNativeWindow();
        refresh.Start();
    }

    // Also used by the published smoke check: real WPF content, measured without fixed boxes.
    internal Size RenderContent(long remaining, SessionState sessionState, TimerSettings timerSettings, long elapsedMilliseconds,
        double maximumWidthDip = double.PositiveInfinity)
    {
        var appearance = OverlayAppearance.For(remaining, sessionState, timerSettings, elapsedMilliseconds);
        text.FontSize = appearance.FontSize > 0 ? appearance.FontSize : 22;
        text.Foreground = appearance.Tone switch
        {
            OverlayTone.Red => new SolidColorBrush(Color.FromRgb(255, 94, 100)),
            OverlayTone.Yellow => new SolidColorBrush(Color.FromRgb(255, 215, 92)),
            OverlayTone.Urgent => Brushes.Orange,
            _ => Brushes.White
        };
        text.Text = appearance.Centered ? "Время вышло"
            : sessionState == SessionState.GraceCountdown ? $"Время на сегодня закончилось\nЗакрытие через {Ui.Time(remaining)}"
            : Ui.Time(remaining);
        text.TextWrapping = sessionState == SessionState.GraceCountdown ? TextWrapping.Wrap : TextWrapping.NoWrap;
        text.MaxWidth = Math.Max(1, maximumWidthDip - 2 * OverlayLayout.HorizontalPadding);
        // WPF can defer parent invalidation until its layout pass. Measure the changed text first,
        // otherwise a font-size transition can reuse the preceding smaller frame for one tick.
        text.InvalidateMeasure();
        text.Measure(new Size(text.MaxWidth, double.PositiveInfinity));
        frame.InvalidateMeasure();
        frame.Measure(new Size(maximumWidthDip, double.PositiveInfinity));
        return frame.DesiredSize;
    }

    private void RefreshNativeWindow()
    {
        if (!IsVisible || positioning) return;
        positioning = true;
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (gameWindow != 0 && IsWindow(gameWindow)) lastMonitor = MonitorFromWindow(gameWindow, 2);
            if (lastMonitor == 0) lastMonitor = MonitorFromWindow(0, 1);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(lastMonitor, ref info))
            {
                lastMonitor = MonitorFromWindow(0, 1);
                if (!GetMonitorInfo(lastMonitor, ref info)) return;
            }
            // Move into the target monitor before obtaining its window DPI. Let WPF process WM_DPICHANGED.
            if (MonitorFromWindow(hwnd, 2) != lastMonitor)
                Position(hwnd, info.Monitor.Left + 20, info.Monitor.Top + 20, 0, 0, SwpNoSize);
            var scale = Math.Max(96u, GetDpiForWindow(hwnd)) / 96.0;
            var monitor = new PixelBounds(info.Monitor.Left, info.Monitor.Top,
                info.Monitor.Right - info.Monitor.Left, info.Monitor.Bottom - info.Monitor.Top);
            var size = RenderContent(seconds, state, settings, animation.ElapsedMilliseconds,
                Math.Max(1, monitor.Width / scale - 2 * OverlayLayout.EdgeGap));
            Width = size.Width;
            Height = size.Height;
            UpdateLayout();
            var appearance = OverlayAppearance.For(seconds, state, settings, animation.ElapsedMilliseconds);
            var target = OverlayLayout.Place(monitor, scale, size.Width, size.Height, appearance.Centered);
            // A borderless game can reorder its HWND on Alt+Tab. Reassert native topmost every tick,
            // including during graceful close, without activation or a Topmost=false/true flicker.
            Position(hwnd, target.X, target.Y, target.Width, target.Height, 0);
        }
        finally { positioning = false; }
    }

    private void Position(nint hwnd, int x, int y, int width, int height, uint additionalFlags)
    {
        if (!SetWindowPos(hwnd, HwndTopmost, x, y, width, height, SwpNoActivate | SwpNoOwnerZOrder | additionalFlags) && !nativeFailureLogged)
        {
            nativeFailureLogged = true;
            App.Log.Write($"Overlay SetWindowPos failed: {Marshal.GetLastWin32Error()}");
        }
    }

    private nint WindowHook(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == 0x0021) { handled = true; return new nint(3); } // WM_MOUSEACTIVATE / MA_NOACTIVATE
        if (message == 0x0084) { handled = true; return new nint(-1); } // WM_NCHITTEST / HTTRANSPARENT
        return 0;
    }

    internal static bool IsAbove(nint overlay, nint other)
    {
        for (var current = GetWindow(overlay, 2); current != 0; current = GetWindow(current, 2))
            if (current == other) return true;
        return false;
    }
    internal static void RaiseForSmokeTest(nint window) => SetWindowPos(window, HwndTopmost, 0, 0, 0, 0,
        SwpNoActivate | SwpNoOwnerZOrder | SwpNoMove | SwpNoSize);

    [StructLayout(LayoutKind.Sequential)] private struct NativeRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint hwnd, uint command);
}
