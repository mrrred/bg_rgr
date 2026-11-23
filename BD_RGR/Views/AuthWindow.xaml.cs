using System.Windows;
using BD_RGR.ViewModels;

namespace BD_RGR.Views;

public partial class AuthWindow : Window
{
    private readonly AuthViewModel _viewModel;

    public AuthWindow(AuthViewModel viewModel)
    {
        _viewModel = viewModel;
        InitializeComponent();
        DataContext = viewModel;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.SignInAsync(SignInPassword.Password);

    private async void OpenRegistration_Click(object sender, RoutedEventArgs e)
    {
        SignInPassword.Clear();
        await _viewModel.StartRegistrationAsync();
        if (IsVisible)
            RegistrationName.Focus();
    }

    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (await _viewModel.RegisterAsync(RegistrationPassword.Password, RepeatedPassword.Password) && IsVisible)
            ReturnFocusToSignIn(SignInPassword);
    }

    private void BackToSignIn_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.ReturnToSignIn();
        ReturnFocusToSignIn(SignInName);
    }

    private void ReturnFocusToSignIn(UIElement input)
    {
        RegistrationPassword.Clear();
        RepeatedPassword.Clear();
        input.Focus();
    }
}
