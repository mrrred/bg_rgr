using System.Globalization;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class BusinessTableViewModel : ViewModel, IDisposable
{
    private readonly AuthApiClient _api;
    private readonly Session _session;
    private readonly Action<AuthNotice> _sessionEnded;
    private readonly Stack<Location> _history = new();
    private BusinessResource[]? _catalog;
    private BusinessResource? _resource;
    private string? _requestedTitle;
    private DisplayRow[] _rows = [];
    private DisplayRow? _selection;
    private FieldEditor[] _fields = [];
    private RelatedAction[] _related = [];
    private string _search = "";
    private string _sort = "id";
    private bool _descending;
    private int _page;
    private bool _hasMore;
    private string? _filterField;
    private int? _filterId;
    private bool _busy;
    private bool _creating;
    private bool _resetting;
    private bool _disposed;
    private AuthNotice? _status;
    private string? _statusArgument;
    private FieldEditor? _mileageVehicle;
    private DateTime? _fromDate;
    private DateTime? _toDate;

    public BusinessTableViewModel(AuthApiClient api, Session session, Action<AuthNotice> sessionEnded)
    {
        _api = api;
        _session = session;
        _sessionEnded = sessionEnded;
        Translation.Current.LanguageChanged += LanguageChanged;
    }

    public BusinessResource? Resource => _resource;
    public string? VisibleTitleKey => Resource?.TitleKey ?? _requestedTitle;
    public DisplayRow[] Rows => _rows;
    public FieldEditor[] Fields => _fields;
    public RelatedAction[] Related => _related;
    public FieldEditor? MileageVehicle => _mileageVehicle;
    public DateTime? FromDate { get => _fromDate; set => Set(ref _fromDate, value); }
    public DateTime? ToDate { get => _toDate; set => Set(ref _toDate, value); }
    public bool ShowsMileage => Resource?.Name == "vehicle_mileage_log" && _session.Role == Role.GarageEngineer;
    public bool CanGoBack => _history.Count > 0;
    public bool CanGoPrevious => _page > 0;
    public bool CanGoNext => _hasMore;
    public int DisplayPage => _page + 1;
    public bool CanAdd => Resource?.Rights.Contains('C') == true;
    public bool CanSave => _creating ? CanAdd : Selection is not null && Resource?.Rights.Contains('U') == true;
    public bool CanDelete => Selection is not null && Resource?.Rights.Contains('D') == true;
    public bool HasFields => Fields.Length > 0;
    public bool HasRelated => Related.Length > 0;
    public bool ShowsEmpty => !Busy && _status is null && Resource is not null && Rows.Length == 0;
    public bool Busy => _busy;
    public bool IsReady => !Busy;
    public string SortKey => _sort;
    public bool Descending => _descending;
    public string Search { get => _search; set => Set(ref _search, value); }

    public string StatusText
    {
        get
        {
            if (_status is null)
                return "";
            var translations = Translation.Current;
            var message = translations[_status.Key];
            if (_statusArgument is not null)
                return string.Format(translations.Culture, message, _statusArgument);
            if (_status.Field is not { } field)
                return message;
            if (field.StartsWith("Database:", StringComparison.Ordinal))
                return string.Format(translations.Culture, message, field);
            var title = Resource?.Fields.FirstOrDefault(f => f.Name == field)?.TitleKey;
            return title is null ? message : $"{translations[title]} · {message}";
        }
    }

    public DisplayRow? Selection
    {
        get => _selection;
        set
        {
            if (_resetting || !Set(ref _selection, value))
                return;
            _creating = false;
            OpenEditor();
        }
    }

    public Task OpenAsync(string titleKey) => RunAsync(async () =>
    {
        _requestedTitle = titleKey;
        _history.Clear();
        Prepare(null, new Location("", "", "id", false, 0, null, null, null));
        _catalog ??= await _api.ResourcesAsync(_session);
        var resource = _catalog.FirstOrDefault(t => t.TitleKey == titleKey);
        if (resource is null)
            throw new AuthApiException("OperationDenied");
        Prepare(resource, Capture() with { Resource = resource.Name });
        await LoadRowsAsync();
    });

    public Task SearchAsync() => RunAsync(async () =>
    {
        if (Resource is null)
        {
            if (_requestedTitle is { } title)
            {
                _catalog ??= await _api.ResourcesAsync(_session);
                var resource = _catalog.FirstOrDefault(t => t.TitleKey == title)
                    ?? throw new AuthApiException("OperationDenied");
                Prepare(resource, Capture() with { Resource = resource.Name });
            }
            else
                return;
        }
        _page = 0;
        await LoadRowsAsync();
    });

    public Task SortAsync(string key) => RunAsync(async () =>
    {
        _descending = _sort == key && !_descending;
        _sort = key;
        _page = 0;
        await LoadRowsAsync();
    });

    public Task PageAsync(int delta) => RunAsync(async () =>
    {
        if (delta < 0 && !CanGoPrevious || delta > 0 && !CanGoNext)
            return;
        _page += delta;
        await LoadRowsAsync();
    });

    public void NewRow()
    {
        if (!CanAdd || Busy)
            return;
        Selection = null;
        _creating = true;
        SetStatus(null);
        OpenEditor();
        if (Fields.FirstOrDefault(field => field.Field.Name == _filterField) is { } prefilled)
            _ = prefilled.SearchAsync();
    }

    public void Cancel()
    {
        _creating = false;
        SetStatus(null);
        OpenEditor();
    }

    public Task SaveAsync() => RunAsync(async () =>
    {
        if (Resource is not { } table || !CanSave)
            return;

        var values = Fields.ToDictionary(field => field.Field.Name, field => field.StoredValue());
        var id = await _api.SaveRowAsync(_session, table.Name, _creating ? null : Selection?.Id, values);
        _creating = false;
        await LoadRowsAsync(id);
        SetStatus(new("RecordSaved"));
    });

    public Task DeleteAsync() => RunAsync(async () =>
    {
        if (Resource is not { } table || !CanDelete || Selection is not { } row)
            return;

        await _api.DeleteRowAsync(_session, table.Name, row.Id);
        await LoadRowsAsync();
        if (Rows.Length == 0 && _page > 0)
        {
            _page--;
            await LoadRowsAsync();
        }
        SetStatus(new("RecordDeleted"));
    });

    public Task FollowAsync(DisplayRow row, string name) => RunAsync(async () =>
    {
        var field = Resource?.Fields.FirstOrDefault(f => f.Name == name);
        if (field?.Target is not { } target || !row.CanFollow.GetValueOrDefault(name)
            || !int.TryParse(row.Row.Values[name], out var id))
            return;

        if (target == "vehicle")
        {
            var link = await _api.VehicleLinkAsync(_session, id);
            target = link.Resource;
            id = link.Id;
        }
        await NavigateAsync(target, id, null, null);
    });

    public Task RelatedAsync(RelatedAction action) => RunAsync(async () =>
    {
        if (Selection is { } row)
            await NavigateAsync(action.Resource, null, action.Field, row.Id);
    });

    public Task BackAsync() => RunAsync(async () =>
    {
        if (!_history.TryPeek(out var previous))
            return;
        var resource = _catalog!.Single(t => t.Name == previous.Resource);
        var page = await FetchAsync(resource, previous);
        _history.Pop();
        Prepare(resource, previous);
        ApplyPage(page, previous.SelectionId);
    });

    public Task CalculateAsync() => RunAsync(async () =>
    {
        if (!ShowsMileage || !int.TryParse(MileageVehicle?.Value, out var id)
            || FromDate is not { } from || ToDate is not { } to || from > to)
            throw new AuthApiException("InvalidPeriod");

        var result = await _api.CalculateMileageAsync(_session, id, from, to);
        if (result.Mileage is { } mileage)
            SetStatus(new("MileageResult"), mileage.ToString("N0", Translation.Current.Culture));
        else
            SetStatus(new("MileageUnavailable"));
    });

    private async Task NavigateAsync(string target, int? id, string? field, int? filterId)
    {
        var resource = _catalog?.FirstOrDefault(t => t.Name == target);
        if (resource is null)
            throw new AuthApiException("OperationDenied");

        var location = new Location(target, "", "id", false, 0, id, field, filterId);
        var page = id is { } targetId
            ? new BusinessPage(0, false, [await _api.RowAsync(_session, target, targetId)])
            : await FetchAsync(resource, location);
        _history.Push(Capture());
        Prepare(resource, location);
        ApplyPage(page, id);
    }

    private Location Capture() => new(Resource?.Name ?? "", Search, _sort, _descending, _page,
        Selection?.Id, _filterField, _filterId);

    private void Prepare(BusinessResource? resource, Location location)
    {
        _resource = resource;
        _sort = location.Sort;
        _descending = location.Descending;
        _page = location.Page;
        _hasMore = false;
        _filterField = location.FilterField;
        _filterId = location.FilterId;
        Search = location.Search;
        _mileageVehicle = ShowsMileage && resource is not null
            ? MakeEditor(resource, resource.Fields.Single(f => f.Name == "vehicle_id"), null, null, true)
            : null;
        Notify(nameof(Resource));
        Notify(nameof(VisibleTitleKey));
        Notify(nameof(ShowsMileage));
        Notify(nameof(MileageVehicle));
        ApplyPage(new BusinessPage(_page, false, []), null);
    }

    private Task<BusinessPage> FetchAsync(BusinessResource resource, Location location) =>
        _api.TableAsync(_session, resource.Name, location.Search, location.Sort, location.Descending,
            location.Page, location.FilterField, location.FilterId);

    private async Task LoadRowsAsync(int? selectId = null)
    {
        if (Resource is not { } resource)
            return;
        var location = Capture();
        var page = await FetchAsync(resource, location);
        ApplyPage(page, selectId ?? location.SelectionId);
    }

    private void ApplyPage(BusinessPage page, int? selectId)
    {
        _page = page.Page;
        _hasMore = page.HasMore;
        _creating = false;
        _rows = Resource is { } resource && _catalog is { } catalog
            ? page.Rows.Select(row => new DisplayRow(row, resource.Fields, catalog)).ToArray()
            : [];
        _resetting = true;
        Notify(nameof(Rows));
        _resetting = false;
        _selection = _rows.FirstOrDefault(row => row.Id == selectId) ?? _rows.FirstOrDefault();
        Notify(nameof(Selection));
        Notify(nameof(CanGoBack));
        Notify(nameof(CanGoPrevious));
        Notify(nameof(CanGoNext));
        Notify(nameof(DisplayPage));
        Notify(nameof(SortKey));
        Notify(nameof(Descending));
        Notify(nameof(CanAdd));
        Notify(nameof(ShowsEmpty));
        OpenEditor();
    }

    private void OpenEditor()
    {
        if (Resource is not { } resource || Selection is null && !_creating)
        {
            _fields = [];
            _related = [];
        }
        else
        {
            _fields = resource.Fields.Select((field, index) =>
            {
                var row = Selection?.Row;
                var value = row is null
                    ? field.Name == _filterField ? _filterId?.ToString(CultureInfo.InvariantCulture) : field.Default
                    : row.Values[field.Name];
                var section = resource.IsVehicleSubtype
                    ? index == 0 ? "Passport" : index == resource.PassportFieldCount ? "Specifications" : null
                    : null;
                return MakeEditor(resource, field, value, row?.Labels.GetValueOrDefault(field.Name), CanSave, section);
            }).ToArray();
            _related = Selection is null ? [] : RelatedActions(resource.Name)
                .Where(action => _catalog!.Any(t => t.Name == action.Resource)).ToArray();
        }

        Notify(nameof(Fields));
        Notify(nameof(Related));
        Notify(nameof(HasFields));
        Notify(nameof(HasRelated));
        Notify(nameof(CanSave));
        Notify(nameof(CanDelete));
    }

    private FieldEditor MakeEditor(BusinessResource resource, BusinessField field, string? value, string? label, bool editable,
        string? section = null) =>
        new(field, value, label, editable, (query, selected) => _api.LookupAsync(_session, resource.Name, field.Name, query, selected),
            HandleError, section);

    private static RelatedAction[] RelatedActions(string resource) => resource switch
    {
        "employee" =>
        [
            new("employment_record", "employee_id", "EmploymentRecord"),
            new("brigade_membership", "employee_id", "BrigadeMembership"),
            new("brigade", "foreman_id", "ForemanAssignments"),
            new("vehicle_assignment", "employee_id", "VehicleAssignment")
        ],
        "cargo_transportation" => [new("transported_cargo", "cargo_transportation_id", "TransportedCargo")],
        _ => []
    };

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _disposed)
            return;
        _busy = true;
        Notify(nameof(Busy));
        Notify(nameof(IsReady));
        SetStatus(new("AuthWorking"));
        try
        {
            await action();
            if (_status?.Key == "AuthWorking")
                SetStatus(null);
        }
        catch (AuthApiException error)
        {
            HandleError(error.Notice);
        }
        finally
        {
            _busy = false;
            Notify(nameof(Busy));
            Notify(nameof(IsReady));
            Notify(nameof(ShowsEmpty));
        }
    }

    private void HandleError(AuthNotice error)
    {
        if (_disposed)
            return;
        if (error.Key == "SessionExpired")
            _sessionEnded(error);
        else
            SetStatus(error);
    }

    private void SetStatus(AuthNotice? status, string? argument = null)
    {
        _status = status;
        _statusArgument = argument;
        foreach (var field in Fields)
            field.HasError = field.Field.Name == status?.Field;
        Notify(nameof(StatusText));
        Notify(nameof(ShowsEmpty));
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Rows)
            row.RefreshText();
        foreach (var editor in Fields)
            editor.RefreshText();
        MileageVehicle?.RefreshText();
        Notify(nameof(StatusText));
    }

    public void Dispose()
    {
        _disposed = true;
        Translation.Current.LanguageChanged -= LanguageChanged;
    }

    private sealed record Location(string Resource, string Search, string Sort, bool Descending, int Page,
        int? SelectionId, string? FilterField, int? FilterId);
}

public sealed record RelatedAction(string Resource, string Field, string TitleKey);
