using GameLimiter.Core;
using Xunit;
namespace GameLimiter.Core.Tests;

public sealed class OverlayTests
{
    [Theory]
    [InlineData(300, 22)]
    [InlineData(120, 30)]
    [InlineData(60, 48)]
    [InlineData(10, 76)]
    [InlineData(1, 76)]
    public void PositiveCountdownRemainsInCorner(long seconds, double font)
    {
        var appearance = OverlayAppearance.For(seconds, SessionState.Running, new(), 0);
        Assert.True(appearance.Visible);
        Assert.False(appearance.Centered);
        Assert.Equal(font, appearance.FontSize);
    }

    [Fact]
    public void LastMinuteAlternatesRedAndYellowWithoutHiding()
    {
        var red = OverlayAppearance.For(60, SessionState.Running, new(), 0);
        var yellow = OverlayAppearance.For(60, SessionState.Running, new(), 500);
        Assert.Equal(OverlayTone.Red, red.Tone);
        Assert.Equal(OverlayTone.Yellow, yellow.Tone);
        Assert.True(red.Visible && yellow.Visible);
        Assert.False(red.Centered || yellow.Centered);
        Assert.Equal(OverlayTone.Red, OverlayAppearance.For(10, SessionState.Running, new(), 1000).Tone);
        Assert.Equal(OverlayTone.Urgent, OverlayAppearance.For(61, SessionState.Running, new(), 500).Tone);
    }

    [Fact]
    public void OnlyExpiryIsCenteredAndFinishedSessionsHide()
    {
        Assert.True(OverlayAppearance.For(0, SessionState.Closing, new(), 0).Centered);
        Assert.False(OverlayAppearance.For(0, SessionState.Completed, new(), 0).Visible);
        Assert.False(OverlayAppearance.For(301, SessionState.Running, new(), 0).Visible);
        Assert.False(OverlayAppearance.For(long.MaxValue, SessionState.Running, new(), 0).Visible);
        Assert.True(OverlayAppearance.For(450, SessionState.GraceCountdown, new(), 0).Visible);
        Assert.False(OverlayAppearance.For(5, SessionState.GraceCountdown, new(), 0).Centered);
    }

    [Theory]
    [InlineData(1920, 1080, 1)]
    [InlineData(1920, 1080, 1.25)]
    [InlineData(1920, 1080, 1.5)]
    [InlineData(1920, 1080, 2)]
    [InlineData(2560, 1440, 1)]
    [InlineData(2560, 1440, 1.25)]
    [InlineData(2560, 1440, 1.5)]
    [InlineData(2560, 1440, 2)]
    [InlineData(3840, 2160, 1.5)]
    [InlineData(3840, 2160, 2)]
    [InlineData(1280, 720, 1)]
    public void PlacementStaysOnMonitorAtDifferentResolutionsAndDpi(int width, int height, double scale)
    {
        foreach (var origin in new[] { (0, 0), (-width, -height), (1920, 100) })
        {
            var monitor = new PixelBounds(origin.Item1, origin.Item2, width, height);
            var corner = OverlayLayout.Place(monitor, scale, 240, 105, false);
            Assert.Equal(origin.Item1 + width - (int)Math.Ceiling(20 * scale), corner.X + corner.Width);
            Assert.Equal(origin.Item2 + (int)Math.Ceiling(20 * scale), corner.Y);
            Assert.Equal((int)Math.Ceiling(240 * scale), corner.Width);
            var center = OverlayLayout.Place(monitor, scale, 420, 100, true);
            Assert.InRange(Math.Abs((center.X + center.Width / 2.0) - (origin.Item1 + width / 2.0)), 0, 0.5);
            Assert.InRange(Math.Abs((center.Y + center.Height / 2.0) - (origin.Item2 + height / 2.0)), 0, 0.5);
        }
    }

    [Fact]
    public void LargeContentIsClampedToVisibleMonitor()
    {
        var monitor = new PixelBounds(-800, 0, 800, 600);
        var result = OverlayLayout.Place(monitor, 2, 1000, 1000, false);
        Assert.True(result.X >= monitor.X && result.Y >= monitor.Y);
        Assert.True(result.X + result.Width <= 0 && result.Y + result.Height <= 600);
    }
}
