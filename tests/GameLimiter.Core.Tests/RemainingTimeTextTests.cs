using GameLimiter.Core;
using Xunit;
namespace GameLimiter.Core.Tests;

public sealed class RemainingTimeTextTests
{
    [Theory]
    [InlineData(0, "На сегодня время закончилось")]
    [InlineData(1, "Осталось меньше минуты")]
    [InlineData(59, "Осталось меньше минуты")]
    [InlineData(60, "Осталась 1 минута")]
    [InlineData(120, "Осталось 2 минуты")]
    [InlineData(660, "Осталось 11 минут")]
    [InlineData(840, "Осталось 14 минут")]
    [InlineData(1260, "Осталась 21 минута")]
    [InlineData(1320, "Осталось 22 минуты")]
    [InlineData(2100, "Осталось 35 минут")]
    [InlineData(2159, "Осталось 35 минут")]
    public void DisplaysReadableRemainingTime(long seconds, string expected) => Assert.Equal(expected, RemainingTimeText.Format(seconds));
}
