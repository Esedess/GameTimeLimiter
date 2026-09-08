namespace GameLimiter.Infrastructure;

public sealed class LocalLog(string folder)
{
    private readonly object gate = new();
    public void Write(string message)
    {
        try
        {
            lock (gate) { Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, DateTime.Now.ToString("yyyy-MM-dd") + ".log"), $"{DateTime.Now:O} {message}{Environment.NewLine}"); }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.WriteLine("GameLimiter log unavailable: " + ex.Message);
        }
    }
}
