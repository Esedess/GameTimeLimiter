using System.Windows;
using System.Windows.Controls;
using GameLimiter.Infrastructure;
namespace GameLimiter.App;

public sealed class PinWindow : Window
{
    public PinWindow(Store store, bool create = false)
    {
        Title = create ? "Создайте родительский PIN" : "Родительский PIN"; Width = 380; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterScreen;
        var panel = new StackPanel { Margin = new Thickness(24) }; Content = panel;
        panel.Children.Add(Ui.Text(create ? "Новый PIN (4–12 цифр)" : "Введите PIN", 20));
        var pin = new PasswordBox { FontSize = 24, MaxLength = 12, Margin = new Thickness(0, 8, 0, 8) }; panel.Children.Add(pin);
        var confirm = new PasswordBox { FontSize = 24, MaxLength = 12, Margin = new Thickness(0, 8, 0, 8) };
        if (create) { panel.Children.Add(Ui.Text("Повторите PIN")); panel.Children.Add(confirm); }
        var error = Ui.Text(""); panel.Children.Add(error);
        var ok = Ui.Button("Продолжить", () =>
        {
            if (create)
            {
                if (pin.Password != confirm.Password) { error.Text = "PIN не совпадают."; return; }
                store.Set("ParentPin", PinService.Hash(pin.Password));
            }
            else if (!PinService.Verify(pin.Password, store.Get("ParentPin"))) { error.Text = "Неверный PIN."; pin.Clear(); return; }
            DialogResult = true;
        }); ok.IsDefault = true; panel.Children.Add(ok); Loaded += (_, _) => pin.Focus();
    }
}
