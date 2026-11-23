using System.ComponentModel;
using System.Windows.Markup;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class MainViewModel : ViewModel, IDisposable
{
    private readonly AuthApiClient _accounts;
    private readonly Action<string, AuthNotice?> _returnToSignIn;
    private MenuEntry _activeEntry = null!;
    private bool _isAccountBusy;
    private AuthNotice? _accountNotice;

    public MainViewModel(Session session, AuthApiClient accounts, ProfileSettings preferences,
        Action<string, AuthNotice?> returnToSignIn)
    {
        Session = session;
        _accounts = accounts;
        _returnToSignIn = returnToSignIn;
        Preferences = preferences;
        BackToMenu = new Command(() => _activeEntry.Parent?.Activate.Execute(null));
        Analytics = new AnalyticsViewModel(accounts, session, EndSession);
        Documents = new DocumentsViewModel(accounts, session, EndSession);
        Documents.PropertyChanged += DocumentsChanged;
        DataTable = new BusinessTableViewModel(accounts, session, notice => _returnToSignIn(Session.Username, notice));
        DataTable.PropertyChanged += TableChanged;
        Menu = WorkspaceMenu.CreateGroups(session.Role, Activate);
        Pages = WorkspaceMenu.CreatePages(session.Role, Activate);
        Translation.Current.LanguageChanged += OnLanguageChanged;
        Activate(Menu[0].Entries[0]);
    }

    public Session Session { get; }
    public ProfileSettings Preferences { get; }
    public DocumentsViewModel Documents { get; }
    public AnalyticsViewModel Analytics { get; }
    public Command BackToMenu { get; }
    public BusinessTableViewModel DataTable { get; }
    public IReadOnlyList<MenuGroup> Menu { get; }
    public IReadOnlyList<MenuEntry> Pages { get; }
    public string TitleKey => ShowsTable ? DataTable.VisibleTitleKey ?? _activeEntry.TitleKey : _activeEntry.TitleKey;
    public IReadOnlyList<MenuEntry> PageChoices => _activeEntry.Children;
    public bool ShowsTable => _activeEntry.IsResource;
    public bool ShowsSettings => _activeEntry.TitleKey == "Settings";
    public bool ShowsDocuments => DocumentsViewModel.TemplateFor(_activeEntry.TitleKey) is not null;
    public bool ShowsAnalytics => _activeEntry.TitleKey == "Analytics";
    public bool ShowsHelp => _activeEntry.TitleKey == "UserGuide";
    public bool ShowsAbout => _activeEntry.TitleKey == "About";
    public bool ShowsPasswordChange => _activeEntry.TitleKey == "ChangePassword";
    public bool ShowsPageChoices => PageChoices.Count > 0;
    public bool HasParentPage => _activeEntry.Parent is not null;
    public bool CanNavigate => !_isAccountBusy && !DataTable.Busy && Documents.IsReady;

    private void EndSession(AuthNotice notice) => _returnToSignIn(Session.Username, notice);

    public XmlLanguage UiLanguage => XmlLanguage.GetLanguage(Translation.Current.Culture.Name);

    public bool CanUseAccountActions => CanNavigate;
    public bool HasAccountNotice => AccountNotice is not null;
    public bool HasSidebarNotice => HasAccountNotice && !ShowsPasswordChange;

    public AuthNotice? AccountNotice
    {
        get => _accountNotice;
        private set
        {
            if (Set(ref _accountNotice, value))
            {
                Notify(nameof(HasAccountNotice));
                Notify(nameof(HasSidebarNotice));
            }
        }
    }

    public Task SignOutAsync() => RunAccountActionAsync(() => _accounts.SignOutAsync(Session), null);

    public Task ChangePasswordAsync(string currentPassword, string newPassword, string repeatedPassword)
    {
        if (!ShowsPasswordChange || _isAccountBusy)
            return Task.CompletedTask;

        if (string.IsNullOrWhiteSpace(currentPassword))
            AccountNotice = new("InvalidCurrentPassword");
        else if (string.IsNullOrWhiteSpace(newPassword))
            AccountNotice = new("RequiredNewPassword");
        else if (newPassword != repeatedPassword)
            AccountNotice = new("PasswordMismatch");
        else
            return RunAccountActionAsync(
                () => _accounts.ChangePasswordAsync(Session, currentPassword, newPassword, repeatedPassword),
                new AuthNotice("PasswordChanged"));

        return Task.CompletedTask;
    }

    private async Task RunAccountActionAsync(Func<Task> action, AuthNotice? success)
    {
        if (!CanNavigate)
            return;

        _isAccountBusy = true;
        Notify(nameof(CanUseAccountActions));
        Notify(nameof(CanNavigate));
        AccountNotice = new("AuthWorking");
        try
        {
            await action();
            _returnToSignIn(Session.Username, success);
        }
        catch (AuthApiException error)
        {
            if (error.Notice.Key == "SessionExpired")
                _returnToSignIn(Session.Username, error.Notice);
            else
                AccountNotice = error.Notice;
        }
        finally
        {
            _isAccountBusy = false;
            Notify(nameof(CanUseAccountActions));
        Notify(nameof(CanNavigate));
        }
    }

    private async void Activate(MenuEntry entry)
    {
        if (!CanNavigate || _activeEntry == entry)
            return;

        if (_activeEntry is not null)
        {
            if (ShowsAnalytics)
                Analytics.Stop();
            _activeEntry.IsActive = false;
            if (_activeEntry.Parent is { } oldParent)
                oldParent.IsActive = false;
        }

        _activeEntry = entry;
        AccountNotice = null;
        entry.IsActive = true;
        if (entry.Parent is { } parent)
            parent.IsActive = true;

        Notify(nameof(TitleKey));
        Notify(nameof(ShowsTable));
        Notify(nameof(ShowsSettings));
        Notify(nameof(ShowsDocuments));
        Notify(nameof(ShowsAnalytics));
        Notify(nameof(ShowsHelp));
        Notify(nameof(ShowsAbout));
        Notify(nameof(ShowsPasswordChange));
        Notify(nameof(PageChoices));
        Notify(nameof(ShowsPageChoices));
        Notify(nameof(HasParentPage));
        if (DocumentsViewModel.TemplateFor(entry.TitleKey) is { } template)
            Documents.Open(template);
        if (ShowsAnalytics)
            await Analytics.StartAsync();

        if (entry.IsResource && DataTable.Resource?.TitleKey != entry.TitleKey)
            await DataTable.OpenAsync(entry.TitleKey);
    }

    private void TableChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BusinessTableViewModel.VisibleTitleKey))
            Notify(nameof(TitleKey));
        if (e.PropertyName == nameof(BusinessTableViewModel.Busy))
        {
            Notify(nameof(CanNavigate));
            Notify(nameof(CanUseAccountActions));
        }
    }

    private void OnLanguageChanged(object? sender, EventArgs e) => Notify(nameof(UiLanguage));

    private void DocumentsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DocumentsViewModel.IsReady))
        {
            Notify(nameof(CanNavigate));
            Notify(nameof(CanUseAccountActions));
        }
    }

    public void Dispose()
    {
        Translation.Current.LanguageChanged -= OnLanguageChanged;
        Analytics.Dispose();
        DataTable.PropertyChanged -= TableChanged;
        DataTable.Dispose();
        Documents.PropertyChanged -= DocumentsChanged;
        Documents.Dispose();
    }
}
