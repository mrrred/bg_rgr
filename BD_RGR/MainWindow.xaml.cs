using System.Windows;
using BD_RGR.ViewModels;
using BD_RGR.Views;

namespace BD_RGR;

public partial class MainWindow : Window
{
    private AnalyticsWindow? _analytics;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is MainViewModel previous)
                previous.AnalyticsRequested -= ShowAnalytics;
            if (e.NewValue is MainViewModel current)
                current.AnalyticsRequested += ShowAnalytics;
        };
        Closed += (_, _) =>
        {
            if (DataContext is MainViewModel model)
                model.AnalyticsRequested -= ShowAnalytics;
        };
    }

    private void Analytics_Click(object sender, RoutedEventArgs e) => ShowAnalytics();

    private void ShowAnalytics()
    {
        if (_analytics is not null)
        {
            if (_analytics.WindowState == WindowState.Minimized)
                _analytics.WindowState = WindowState.Normal;
            _analytics.Activate();
            return;
        }
        if (DataContext is not MainViewModel model)
            return;
        _analytics = new AnalyticsWindow(model.CreateAnalytics()) { Owner = this };
        _analytics.Closed += (_, _) => _analytics = null;
        _analytics.Show();
    }

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
