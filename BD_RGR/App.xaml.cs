using System.ComponentModel;
using System.Windows;
using BD_RGR.Models;
using BD_RGR.Services;
using BD_RGR.ViewModels;
using BD_RGR.Views;

namespace BD_RGR;

public partial class App : Application
{
    private AuthApiClient _accounts = null!;
    private readonly ProfileSettings _preferences;

    public App() : this(new ProfileSettings()) { }

    internal App(ProfileSettings preferences) => _preferences = preferences;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        try
        {
            _preferences.Initialize();
            var settings = ClientSettings.Load();
            _accounts = new AuthApiClient(settings.ApiBaseUrl);
        }
        catch (ClientSettingsException error)
        {
            var text = Translation.Current;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            AppDialog.ShowError(text["AppName"], string.Format(text.Culture, text["ClientSettingsInvalid"], error.Field));
            Shutdown(1);
            return;
        }

        ShowSignIn("", null);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _accounts?.Dispose();
        _preferences.Dispose();
        base.OnExit(e);
    }

    private void ShowSignIn(string username, AuthNotice? notice) =>
        ReplaceWindow(new AuthWindow(new AuthViewModel(_accounts, ShowWorkspace, username, notice)));

    private void ShowWorkspace(Session session)
    {
        var viewModel = new MainViewModel(session, _accounts, _preferences, ShowSignIn);
        var window = new MainWindow { DataContext = viewModel };
        window.Closed += (_, _) => viewModel.Dispose();
        AttachSessionCleanup(window, session);
        ReplaceWindow(window);
    }

    private void AttachSessionCleanup(MainWindow window, Session session)
    {
        var closing = false;
        var finished = false;
        window.Closing += CloseAsync;

        async void CloseAsync(object? sender, CancelEventArgs e)
        {
            if (MainWindow != window || finished)
                return;

            e.Cancel = true;
            if (closing)
                return;

            closing = true;
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            try
            {
                await _accounts.SignOutAsync(session, deadline.Token);
            }
            catch (AuthApiException)
            {
                // The server expires any session that cannot be closed while the connection is down.
            }
            finally
            {
                finished = true;
                // Schedule the second close outside the active Closing event, even if the request finished synchronously.
                _ = window.Dispatcher.InvokeAsync(() =>
                {
                    if (MainWindow == window)
                        window.Close();
                });
            }
        }
    }

    private void ReplaceWindow(Window next)
    {
        var previous = MainWindow;
        MainWindow = next;
        next.Show();
        if (previous != next)
            previous?.Close();
    }
}
