using System.Windows;
using BD_RGR.ViewModels;

namespace BD_RGR;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private async void SignOut_Click(object sender, RoutedEventArgs e) =>
        await ((MainViewModel)DataContext).SignOutAsync();

    private async void ChangePassword_Click(object sender, RoutedEventArgs e) =>
        await ((MainViewModel)DataContext).ChangePasswordAsync(
            CurrentPasswordInput.Password, NewPasswordInput.Password, RepeatNewPasswordInput.Password);

    private void PasswordForm_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is false)
        {
            CurrentPasswordInput?.Clear();
            NewPasswordInput?.Clear();
            RepeatNewPasswordInput?.Clear();
        }
    }
}
