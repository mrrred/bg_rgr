using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class AuthViewModel(AuthApiClient accounts, Action<Session> enterWorkspace,
    string username = "", AuthNotice? notice = null) : ViewModel
{
    private string _username = username;
    private string _registrationName = "";
    private RegistrationRole? _selectedRole;
    private IReadOnlyList<RegistrationRole> _roles = [];
    private bool _isRegistering;
    private bool _isBusy;
    private AuthNotice? _notice = notice;

    public IReadOnlyList<RegistrationRole> Roles
    {
        get => _roles;
        private set => Set(ref _roles, value);
    }

    public string Username
    {
        get => _username;
        set => Set(ref _username, value);
    }

    public string RegistrationName
    {
        get => _registrationName;
        set => Set(ref _registrationName, value);
    }

    public RegistrationRole? SelectedRole
    {
        get => _selectedRole;
        set => Set(ref _selectedRole, value);
    }

    public bool IsRegistering
    {
        get => _isRegistering;
        private set
        {
            if (Set(ref _isRegistering, value))
                Notify(nameof(IsSigningIn));
        }
    }

    public bool IsSigningIn => !IsRegistering;

    public bool CanInteract => !_isBusy;

    public AuthNotice? Notice
    {
        get => _notice;
        private set => Set(ref _notice, value);
    }

    public Task StartRegistrationAsync()
    {
        RegistrationName = Username.Trim();
        SelectedRole = null;
        Roles = [];
        IsRegistering = true;
        return RunAsync(async () =>
        {
            Roles = await accounts.GetRolesAsync();
            Notice = null;
        });
    }

    public void ReturnToSignIn()
    {
        Notice = null;
        IsRegistering = false;
    }

    public Task SignInAsync(string password) => RunAsync(async () =>
    {
        var session = await accounts.SignInAsync(Username.Trim(), password);
        enterWorkspace(session);
    });

    public Task<bool> RegisterAsync(string password, string repeatedPassword)
    {
        var login = RegistrationName.Trim();
        if (login.Length == 0 || string.IsNullOrWhiteSpace(password))
        {
            Notice = new("RequiredCredentials");
            return Task.FromResult(false);
        }
        if (password != repeatedPassword)
        {
            Notice = new("PasswordMismatch");
            return Task.FromResult(false);
        }
        if (SelectedRole is not { } role)
        {
            Notice = new(Roles.Count == 0 ? "RoleCatalogUnavailable" : "RequiredRole");
            return Task.FromResult(false);
        }

        return RunAsync(async () =>
        {
            await accounts.RegisterAsync(login, password, repeatedPassword, role.Id);
            Username = login;
            IsRegistering = false;
            Notice = new("Registered");
        });
    }

    private async Task<bool> RunAsync(Func<Task> action)
    {
        if (_isBusy)
            return false;

        _isBusy = true;
        Notify(nameof(CanInteract));
        Notice = new("AuthWorking");
        try
        {
            await action();
            return true;
        }
        catch (AuthApiException error)
        {
            Notice = error.Notice;
            return false;
        }
        finally
        {
            _isBusy = false;
            Notify(nameof(CanInteract));
        }
    }
}
