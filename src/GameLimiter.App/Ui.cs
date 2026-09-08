using System.Windows;
using System.Windows.Controls;
namespace GameLimiter.App;

internal static class Ui
{
    public static TextBlock Text(string text, double size = 14) => new() { Text = text, FontSize = size, Margin = new Thickness(0, 5, 0, 5), TextWrapping = TextWrapping.Wrap };
    public static Button Button(string text, Action action)
    {
        var button = new Button { Content = text }; button.Click += (_, _) => Guard(action); return button;
    }
    public static void Guard(Action action) { try { action(); } catch (Exception ex) { App.Log.Write("UI error: " + ex); MessageBox.Show(ex.Message, "GameLimiter", MessageBoxButton.OK, MessageBoxImage.Error); } }
    public static TextBox Field(Panel panel, string label, string value = "") { panel.Children.Add(Text(label)); var box = new TextBox { Text = value }; panel.Children.Add(box); return box; }
    public static CheckBox Check(Panel panel, string label, bool value) { var box = new CheckBox { Content = label, IsChecked = value, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(box); return box; }
    public static int Number(TextBox field, int min, int max)
    {
        if (!int.TryParse(field.Text, out var value) || value < min || value > max) throw new ArgumentException($"Введите целое число от {min} до {max}."); return value;
    }
    public static string Time(long seconds) { var sign = seconds < 0 ? "−" : ""; seconds = Math.Abs(seconds); return $"{sign}{seconds / 60:00}:{seconds % 60:00}"; }
}
