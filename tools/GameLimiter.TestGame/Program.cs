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
            Process.Start(new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory,"GameLimiter.TestGame.exe"),args.Contains("--headless")?"--headless":"--ignore-close") { UseShellExecute=false,WindowStyle=ProcessWindowStyle.Hidden }); return;
        }
        if(args.Contains("--headless")) { Thread.Sleep(Timeout.Infinite); return; }
        var window=new Window { Title="GameLimiter — тестовая игра",Width=720,Height=440,Content=new TextBlock { Text="Тестовая игра\n\nМожно менять лимит в GameLimiter.\n--ignore-close: проверка принудительного завершения.\n\nЗакрытие этого окна завершает обычную сессию.",FontSize=24,Margin=new Thickness(30),TextWrapping=TextWrapping.Wrap } };
        if(args.Contains("--ignore-close")) window.Closing+=(_,e)=>e.Cancel=true;
        new Application().Run(window);
    }
}
