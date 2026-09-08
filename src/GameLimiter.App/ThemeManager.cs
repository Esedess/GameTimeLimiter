using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using GameLimiter.Infrastructure;
namespace GameLimiter.App;

internal static class ThemeManager
{
    public static string Current { get; private set; } = "Dark";
    public static void Load(Store store) => Apply(store.Get("Theme") == "Light" ? "Light" : "Dark");
    public static void Save(Store store, string theme)
    {
        if (theme is not ("Light" or "Dark")) throw new ArgumentException("Неизвестная тема.");
        store.Set("Theme", theme);
        Apply(theme);
    }
    public static void Apply(string theme)
    {
        Current = theme;
        var dark = theme == "Dark";
        var colors = new Dictionary<string, string>
        {
            ["AppBackground"] = dark ? "#171D27" : "#F2F5F9",
            ["AppSurface"] = dark ? "#202835" : "#FFFFFF",
            ["AppText"] = dark ? "#E8EDF5" : "#1D2939",
            ["AppInput"] = dark ? "#171F2B" : "#FFFFFF",
            ["AppButton"] = dark ? "#2C384A" : "#E5ECF4",
            ["AppBorder"] = dark ? "#53647A" : "#A5B3C4",
            ["AppHover"] = dark ? "#384A61" : "#D6E3F2",
            ["AppSelection"] = dark ? "#304C69" : "#C8DEF5",
            ["AppAccent"] = dark ? "#85BFFF" : "#2563A0"
        };
        foreach (var (key, value) in colors)
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(value));
            brush.Freeze();
            Application.Current.Resources[key] = brush;
        }
        foreach (Window window in Application.Current.Windows) UpdateTitleBar(window);
    }
    public static void UpdateTitleBar(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == 0) return;
        var dark = Current == "Dark" ? 1 : 0;
        _ = DwmSetWindowAttribute(handle, 20, ref dark, sizeof(int));
    }
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint window, int attribute, ref int value, int size);
}
