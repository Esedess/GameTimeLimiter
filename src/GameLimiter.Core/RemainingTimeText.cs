namespace GameLimiter.Core;

public static class RemainingTimeText
{
    public static string Format(long seconds)
    {
        if (seconds <= 0) return "На сегодня время закончилось";
        if (seconds < 60) return "Осталось меньше минуты";
        var minutes = seconds / 60;
        var word = minutes % 100 is >= 11 and <= 14 ? "минут"
            : minutes % 10 == 1 ? "минута" : minutes % 10 is >= 2 and <= 4 ? "минуты" : "минут";
        return $"{(word == "минута" ? "Осталась" : "Осталось")} {minutes} {word}";
    }
}
