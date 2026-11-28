using System.Globalization;
using System.Text.Json;
using System.Windows.Threading;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class AnalyticsViewModel : ViewModel, IDisposable
{
    private static readonly Dictionary<string, (string Title, string All)> FilterTitles = new()
    {
        ["route"] = ("Route", "DashboardAllRoutes"),
        ["truck"] = ("Truck", "DashboardAllTrucks"),
        ["cargoType"] = ("CargoType", "DashboardAllCargoTypes"),
        ["garage"] = ("Garage", "DashboardAllGarages"),
        ["category"] = ("DashboardCategory", "DashboardAllCategories"),
        ["status"] = ("Status", "DashboardAllStatuses"),
        ["vehicle"] = ("Vehicle", "DashboardAllVehicles"),
        ["brigade"] = ("Brigade", "DashboardAllTeams"),
        ["component"] = ("Component", "DashboardAllComponents"),
        ["department"] = ("Department", "DashboardAllDepartments"),
        ["event"] = ("FieldHrEventType", "DashboardAllEvents"),
        ["grouping"] = ("DashboardGrouping", "DashboardByDate")
    };

    private sealed class ScreenState(DashboardLink link)
    {
        public DashboardLink Link { get; } = link;
        public DashboardRequest? Applied { get; set; }
        public DashboardResult? Result { get; set; }
        public string? Content { get; set; }
        public Dictionary<string, DashboardOption[]>? Options { get; set; }
    }

    private readonly DashboardData _data;
    private readonly Action<AuthNotice> _sessionEnded;
    private readonly Func<string, Task> _openWorkspace;
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<string, ScreenState> _screens = new();
    private ScreenState? _current;
    private CancellationTokenSource? _operation;
    private AuthNotice? _notice;
    private DateTime? _from;
    private DateTime? _to;
    private bool _invalidPeriod;
    private bool _ready;
    private bool _disposed;
    private bool _settingFilters;
    private DashboardFilter[] _filters = [];
    private DashboardGroup[] _groups = [];

    public AnalyticsViewModel(AuthApiClient api, Session session, Action<AuthNotice> sessionEnded,
        Func<string, Task> openWorkspace)
    {
        _data = new DashboardData(api, session);
        _sessionEnded = sessionEnded;
        _openWorkspace = openWorkspace;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += OnTick;
        Translation.Current.LanguageChanged += OnLanguageChanged;
    }

    public DashboardGroup[] Groups => _groups;
    public bool HasGroups => _groups.Length > 0;
    public DashboardFilter[] Filters => _filters;
    public bool HasScreen => _current is not null;
    public bool HasPeriod => _current?.Link.Screen.HasPeriod == true;
    public string ScreenTitle => _current?.Link.Title ?? "";
    public DateTime? From { get => _from; set => Set(ref _from, value); }
    public DateTime? To { get => _to; set => Set(ref _to, value); }
    public bool IsReady { get => _ready; private set => Set(ref _ready, value); }
    public bool HasInvalidPeriod => _invalidPeriod;
    public string FilterError => _invalidPeriod ? Translation.Current["AnalyticsPeriodInvalid"] : "";

    public DashboardCardItem[] Cards => _current?.Result?.Cards.Select(card => new DashboardCardItem(card,
        card.Target is { } target && _screens.ContainsKey(target) ? new Command(() => OpenCard(card)) : null)).ToArray() ?? [];
    public DashboardPlot[] Charts => _current?.Result?.Charts ?? [];
    public DashboardTable[] Tables => _current?.Result?.Tables ?? [];
    public IEnumerable<DashboardTable> HeatTables => Tables.Where(table => table.Heat);
    public IEnumerable<DashboardTable> SummaryTables => Tables.Where(table => !table.Heat);
    public string ChartScope
    {
        get
        {
            var culture = Translation.Current.Culture;
            if (_current?.Applied is { From: { } from, To: { } to })
                return $"{from.ToString("d", culture)} – {to.ToString("d", culture)}";
            return _current?.Result?.UpdatedAt.LocalDateTime.ToString("d", culture) ?? "";
        }
    }

    public string Status
    {
        get
        {
            var text = Translation.Current;
            if (_notice is { } notice)
                return notice.Field is { } field ? string.Format(text.Culture, text[notice.Key], field) : text[notice.Key];
            return _current?.Result is { } result
                ? string.Format(text.Culture, text["AnalyticsUpdated"], result.UpdatedAt.LocalDateTime.ToString("G", text.Culture))
                : "";
        }
    }

    public async Task StartAsync(string? screen = null)
    {
        if (_disposed)
            return;
        var alreadyActive = _timer.IsEnabled;
        _timer.Start();
        await LoadMenuAsync();
        if (!_timer.IsEnabled)
            return;
        if (screen is not null && _screens.TryGetValue(screen, out var selected) && selected != _current)
        {
            await OpenAsync(selected);
            return;
        }
        if (_current is { } current)
        {
            if (alreadyActive && screen is not null)
                return;
            current.Link.IsActive = true;
            if (current.Options is null)
                await OpenAsync(current, current.Applied);
            else
                await RefreshAsync(current, current.Applied ?? Draft(), commit: current.Applied is null);
            return;
        }

        if (_screens.Values.FirstOrDefault() is { } first)
            await OpenAsync(first);
    }

    public async Task LoadMenuAsync()
    {
        if (_disposed || _screens.Count > 0 || _operation is not null)
            return;
        await RunAsync(async token =>
        {
            var screens = await _data.ScreensAsync(token);
            token.ThrowIfCancellationRequested();
            foreach (var screen in screens)
            {
                var link = new DashboardLink(screen, async () => await _openWorkspace(screen.Name));
                _screens[screen.Name] = new(link);
            }
            BuildGroups();
            if (_screens.Count == 0)
                _notice = new("OperationDenied");
        });
    }

    public void Stop()
    {
        _timer.Stop();
        _operation?.Cancel();
        if (_current is { } current)
            current.Link.IsActive = false;
    }

    public Task ApplyAsync()
    {
        if (_current is not { } current)
            return Task.CompletedTask;
        var request = Draft();
        _invalidPeriod = HasPeriod && (request.From is not { } from || request.To is not { } to || to < from);
        ErrorChanged();
        return _invalidPeriod ? Task.CompletedTask : RefreshAsync(current, request, commit: true);
    }

    public Task ResetAsync() => _current is { } current ? OpenAsync(current, Defaults(current.Link.Screen)) : StartAsync();

    public Task DrillAsync(string filter, string choice)
    {
        if (_current is not { } current || !current.Link.Screen.Filters.Contains(filter))
            return Task.CompletedTask;
        var applied = current.Applied ?? Defaults(current.Link.Screen);
        var values = new Dictionary<string, string?>(applied.Values) { [filter] = choice };
        var next = applied with { Values = values };
        SetDraft(current, next);
        return ApplyAsync();
    }

    public void ShowFileError()
    {
        _notice = new("ExportFileError");
        Notify(nameof(Status));
    }

    private async void OpenCard(DashboardCard card)
    {
        if (_current?.Applied is not { } source || !_screens.TryGetValue(card.Target!, out var target))
            return;
        var previous = target.Applied ?? Defaults(target.Link.Screen);
        var values = new Dictionary<string, string?>(previous.Values);
        if (card.Filter is { } filter)
            values[filter] = card.Choice;
        var next = previous with { Values = values };
        if (target.Link.Screen.HasPeriod && source.From is not null)
            next = next with { From = source.From, To = source.To };
        await OpenAsync(target, next);
    }

    private async Task OpenAsync(ScreenState state, DashboardRequest? filters = null)
    {
        if (_current is not null)
            _current.Link.IsActive = false;
        _current = state;
        state.Link.IsActive = true;
        _invalidPeriod = false;
        var request = filters ?? state.Applied ?? Defaults(state.Link.Screen);
        SetDraft(state, request);
        Notify(nameof(HasScreen));
        Notify(nameof(HasPeriod));
        Notify(nameof(ScreenTitle));
        ErrorChanged();
        ResultChanged();

        await RunAsync(async token =>
        {
            state.Options = await Task.Run(() => _data.OptionsAsync(state.Link.Screen, token), token);
            token.ThrowIfCancellationRequested();
            SetDraft(state, request);
            await LoadAsync(state, Draft(), commit: true, token);
        });
    }

    private Task RefreshAsync(ScreenState state, DashboardRequest request, bool commit) =>
        RunAsync(token => LoadAsync(state, request, commit, token), keepFiltersEnabled: !commit);

    private async Task LoadAsync(ScreenState state, DashboardRequest request, bool commit, CancellationToken token)
    {
        var result = await Task.Run(() => _data.LoadAsync(state.Link.Screen.Name, request, token), token);
        token.ThrowIfCancellationRequested();
        if (state.Link.Screen.Name == "mileage")
        {
            var adjusted = request.Values.GetValueOrDefault("vehicle") != result.SelectedVehicle;
            if (adjusted)
            {
                state.Options = await Task.Run(() => _data.OptionsAsync(state.Link.Screen, token), token);
                token.ThrowIfCancellationRequested();
                request.Values["vehicle"] = result.SelectedVehicle;
                if (!commit)
                    UpdateVehicles(Filters.FirstOrDefault(filter => filter.Name == "vehicle")?.Value);
            }
        }
        if (commit || state.Applied is null)
        {
            state.Applied = request;
            SetDraft(state, request);
        }
        var content = JsonSerializer.Serialize(new { result.Cards, result.Charts, result.Tables });
        if (state.Content != content)
        {
            state.Content = content;
            state.Result = result;
            ResultChanged();
        }
        else if (state.Result is { } current)
            state.Result = current with { UpdatedAt = result.UpdatedAt };
        Notify(nameof(ChartScope));
    }

    private async Task RunAsync(Func<CancellationToken, Task> action, bool keepFiltersEnabled = false)
    {
        if (_disposed)
            return;
        _operation?.Cancel();
        using var operation = new CancellationTokenSource();
        _operation = operation;
        if (!keepFiltersEnabled)
            IsReady = false;
        _notice = _current?.Result is null ? new("AuthWorking") : null;
        Notify(nameof(Status));
        try
        {
            await action(operation.Token);
            if (!operation.IsCancellationRequested && _notice?.Key == "AuthWorking")
                _notice = null;
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested) { }
        catch (AuthApiException error)
        {
            if (!operation.IsCancellationRequested)
            {
                _notice = error.Notice;
                if (error.Notice.Key == "SessionExpired")
                {
                    Stop();
                    _sessionEnded(error.Notice);
                }
            }
        }
        finally
        {
            if (_operation == operation)
            {
                _operation = null;
                IsReady = true;
                Notify(nameof(Status));
            }
        }
    }

    private DashboardRequest Draft() => new(HasPeriod && From is { } from ? DateOnly.FromDateTime(from) : null,
        HasPeriod && To is { } to ? DateOnly.FromDateTime(to) : null,
        Filters.ToDictionary(filter => filter.Name, filter => filter.Value));

    private static DashboardRequest Defaults(DashboardScreen screen)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return new(screen.HasPeriod ? today.AddDays(-29) : null, screen.HasPeriod ? today : null,
            screen.Filters.ToDictionary(key => key, key => key == "grouping" ? "dates" : (string?)null));
    }

    private void SetDraft(ScreenState state, DashboardRequest request)
    {
        _settingFilters = true;
        try
        {
            From = request.From?.ToDateTime(TimeOnly.MinValue);
            To = request.To?.ToDateTime(TimeOnly.MinValue);
            _filters = state.Link.Screen.Filters.Select(name =>
            {
                var title = FilterTitles[name];
                var useAll = name != "grouping" && !(name == "vehicle" && state.Link.Screen.Name == "mileage");
                var choices = state.Options?.GetValueOrDefault(name) ?? [];
                var input = new DashboardFilter(name, title.Title, useAll ? [new(null, title.All, true, null, null), .. choices] : choices,
                    request.Values.GetValueOrDefault(name));
                if (name is "garage" or "category")
                    input.PropertyChanged += (_, _) => ScopeChanged();
                return input;
            }).ToArray();
            UpdateVehicles(request.Values.GetValueOrDefault("vehicle"));
            Notify(nameof(Filters));
        }
        finally
        {
            _settingFilters = false;
        }
    }

    private void ScopeChanged()
    {
        if (!_settingFilters)
            UpdateVehicles(Filters.FirstOrDefault(input => input.Name == "vehicle")?.Value);
    }

    private void UpdateVehicles(string? selected)
    {
        if (_current is not { Link.Screen.Name: "mileage" } current ||
            Filters.FirstOrDefault(filter => filter.Name == "vehicle") is not { } vehicle)
            return;
        var garage = Filters.Single(filter => filter.Name == "garage").Value;
        var category = Filters.Single(filter => filter.Name == "category").Value;
        var options = current.Options?.GetValueOrDefault("vehicle") ?? [];
        vehicle.SetOptions(options.Where(option => (garage is null || garage == option.GarageId?.ToString(CultureInfo.InvariantCulture)) &&
            (category is null || category == option.Category)).ToArray(), selected);
    }

    private void BuildGroups()
    {
        _groups = _screens.Values.Select(value => value.Link).GroupBy(link => link.Screen.GroupKey)
            .Select(group => new DashboardGroup(group.Key, group.ToArray()))
            .ToArray();
        Notify(nameof(Groups));
        Notify(nameof(HasGroups));
    }

    private void ErrorChanged()
    {
        Notify(nameof(HasInvalidPeriod));
        Notify(nameof(FilterError));
    }

    private void ResultChanged()
    {
        Notify(nameof(Cards));
        Notify(nameof(Charts));
        Notify(nameof(Tables));
        Notify(nameof(HeatTables));
        Notify(nameof(SummaryTables));
    }

    private async void OnTick(object? sender, EventArgs e)
    {
        if (_operation is not null)
            return;
        if (_current is { Applied: { } applied } current)
            await RefreshAsync(current, applied, commit: false);
        else
            await StartAsync();
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        if (_current is { } current)
            SetDraft(current, Draft());
        Notify(nameof(ScreenTitle));
        Notify(nameof(ChartScope));
        ErrorChanged();
        ResultChanged();
        Notify(nameof(Status));
    }

    public void Dispose()
    {
        _disposed = true;
        Stop();
        _timer.Tick -= OnTick;
        Translation.Current.LanguageChanged -= OnLanguageChanged;
    }
}

public sealed record DashboardGroup(string TitleKey, DashboardLink[] Links);

public sealed class DashboardLink(DashboardScreen screen, Action open) : ViewModel
{
    private bool _isActive;
    public DashboardScreen Screen { get; } = screen;
    public string TitleKey => Screen.TitleKey;
    public string Title => Translation.Current[TitleKey];
    public Command Activate { get; } = new(open);
    public bool IsActive { get => _isActive; set => Set(ref _isActive, value); }
}

public sealed record DashboardPick(string? Value, string Text);

public sealed class DashboardFilter : ViewModel
{
    private DashboardPick? _selected;
    private DashboardPick[] _options = [];
    public DashboardFilter(string name, string title, DashboardOption[] options, string? choice)
    {
        Name = name;
        Title = Translation.Current[title];
        SetOptions(options, choice);
    }

    public string Name { get; }
    public string Title { get; }
    public DashboardPick[] Options { get => _options; private set => Set(ref _options, value); }
    public DashboardPick? Selected { get => _selected; set => Set(ref _selected, value); }
    public string? Value => Selected?.Value;

    public void SetOptions(DashboardOption[] options, string? value)
    {
        Options = options.Select(option => new DashboardPick(option.Value, option.Localized ? Translation.Current[option.Label] : option.Label)).ToArray();
        Selected = Options.FirstOrDefault(option => option.Value == value) ?? Options.FirstOrDefault();
    }
}

public sealed record DashboardCardItem(DashboardCard Card, Command? Open)
{
    public string Title => Translation.Current[Card.TitleKey];
    public string Value => Card.Value is { } value ? DashboardFormatting.Number(value) : "—";
    public string Unit => Card.Value is not null && Card.UnitKey is { } key ? Translation.Current[key] : "";
    public string Detail => Card.TitleKey == "DashboardDistance"
        ? Card.Value is null ? Translation.Current["DashboardInsufficientReadings"]
        : $"{Card.StartReading?.ToString("d", Translation.Current.Culture)} → {Card.EndReading?.ToString("d", Translation.Current.Culture)}" : "";
}

internal static class DashboardFormatting
{
    internal static string Number(decimal value) => value.ToString("#,##0.###", Translation.Current.Culture);

    internal static string Label(DashboardLabel label, bool shortDate = false) =>
        label.Localized ? Translation.Current[label.Text] :
        DateOnly.TryParseExact(label.Text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToString(shortDate ? "dd.MM" : "d", Translation.Current.Culture) : label.Text;
}
