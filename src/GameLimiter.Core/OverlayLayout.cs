namespace GameLimiter.Core;

public enum OverlayTone { Normal, Urgent, Red, Yellow }

public readonly record struct OverlayAppearance(bool Visible, bool Centered, double FontSize, OverlayTone Tone)
{
    public static OverlayAppearance For(long seconds, SessionState state, TimerSettings settings, long elapsedMilliseconds)
    {
        if (state is SessionState.Completed or SessionState.Idle or SessionState.Failed ||
            (seconds > settings.First && state != SessionState.GraceCountdown)) return default;
        if (seconds <= 0 || state == SessionState.Closing) return new(true, true, 52, OverlayTone.Yellow);

        // Positive countdowns always remain in the corner, including the final ten seconds.
        var flashing = seconds <= settings.Continuous;
        var tone = flashing ? ((elapsedMilliseconds / 500) % 2 == 0 ? OverlayTone.Red : OverlayTone.Yellow)
            : seconds <= settings.Urgent ? OverlayTone.Urgent : OverlayTone.Normal;
        var font = state == SessionState.GraceCountdown ? 23 : seconds <= settings.Large ? 76
            : flashing ? 48 : seconds <= settings.Urgent ? 30 : 22;
        return new(true, false, font, tone);
    }
}

public readonly record struct PixelBounds(int X, int Y, int Width, int Height);

public static class OverlayLayout
{
    public const double HorizontalPadding = 15;
    public const double VerticalPadding = 10;
    public const double EdgeGap = 20;

    // Origins are already physical desktop coordinates. Scale dimensions, never the origin:
    // monitors to the left/above the primary can have negative origins and different DPI.
    public static PixelBounds Place(PixelBounds monitor, double dpiScale, double widthDip, double heightDip, bool centered)
    {
        if (monitor.Width <= 0 || monitor.Height <= 0 || !double.IsFinite(dpiScale) || dpiScale <= 0 ||
            !double.IsFinite(widthDip) || !double.IsFinite(heightDip) || widthDip <= 0 || heightDip <= 0)
            throw new ArgumentException("Invalid overlay bounds or DPI.");
        var gap = Math.Min((int)Math.Ceiling(EdgeGap * dpiScale), Math.Min(monitor.Width, monitor.Height) / 4);
        var width = (int)Math.Clamp(Math.Ceiling(widthDip * dpiScale), 1, monitor.Width - 2 * gap);
        var height = (int)Math.Clamp(Math.Ceiling(heightDip * dpiScale), 1, monitor.Height - 2 * gap);
        var x = centered ? monitor.X + (monitor.Width - width) / 2 : monitor.X + monitor.Width - width - gap;
        var y = centered ? monitor.Y + (monitor.Height - height) / 2 : monitor.Y + gap;
        return new(x, y, width, height);
    }
}
