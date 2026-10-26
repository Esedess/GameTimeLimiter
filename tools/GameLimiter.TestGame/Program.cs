using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace GameLimiter.TestGame;
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if(args.Contains("--launcher"))
        {
            var info = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"GameLimiter.TestGame.exe")) { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden };
            foreach (var arg in args.Where(a => a != "--launcher")) info.ArgumentList.Add(arg);
            Process.Start(info); return;
        }
        var report = Array.IndexOf(args, "--profile-report");
        if (report >= 0)
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            File.WriteAllLines(args[report + 1], [identity.User!.Value, Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), Environment.GetEnvironmentVariable("USERPROFILE") ?? "", Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)]);
        }
        if(args.Contains("--headless")) { Thread.Sleep(Timeout.Infinite); return; }
        var window=new Window { Title="GameLimiter — тестовая игра",Width=720,Height=440,Content=new TextBlock { Text="Тестовая игра\n\nМожно менять лимит в GameLimiter.\n--ignore-close: проверка принудительного завершения.\n\nЗакрытие этого окна завершает обычную сессию.",FontSize=24,Margin=new Thickness(30),TextWrapping=TextWrapping.Wrap } };
        if(args.Contains("--ignore-close")) window.Closing+=(_,e)=>e.Cancel=true;
        new Application().Run(window);
    }
}
