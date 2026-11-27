using System.Globalization;
using System.Text;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class DocumentsViewModel : ViewModel, IDisposable
{
    private readonly ResourceReports _reports;
    private readonly Action<AuthNotice> _sessionEnded;
    private string _template = "";
    private string _sql = "SELECT id, name FROM public.model ORDER BY id";
    private ReportResult? _result;
    private DocumentRow[] _rows = [];
    private AuthNotice? _notice;
    private bool _busy;
    private bool _disposed;

    public DocumentsViewModel(AuthApiClient api, Session session, Action<AuthNotice> sessionEnded)
    {
        _reports = new ResourceReports(api, session);
        _sessionEnded = sessionEnded;
        Translation.Current.LanguageChanged += LanguageChanged;
    }

    public static string? TemplateFor(string key) => key switch
    {
        "RouteReport" => "routes", "CargoReport" => "cargo", "MileageReport" => "mileage",
        "ManualReport" => "manual", _ => null
    };

    public bool IsManual => _template == "manual";
    public bool IsReady => !_busy;
    public bool CanExport => IsReady && _result is not null;
    public ReportColumn[] Columns => _result?.Columns ?? [];
    public DocumentRow[] Rows => _rows;
    public string Sql { get => _sql; set => Set(ref _sql, value); }

    public string Status => _notice is { } notice
        ? (notice.Field is { } field ? string.Format(Translation.Current.Culture, Translation.Current[notice.Key], field)
            : Translation.Current[notice.Key])
        : "";

    public void Open(string template)
    {
        if (_template == template)
            return;
        _template = template;
        _notice = null;
        ShowResult(null);
        Notify(nameof(IsManual));
        Notify(nameof(Status));
    }

    public Task RunAsync() => ExecuteAsync(async () =>
    {
        ShowResult(null);
        var template = _template;
        var result = await _reports.RunAsync(template, Sql);
        if (template != _template)
            return;
        ShowResult(result);
        _notice = new(result.Truncated ? "ReportTruncated" : result.Rows.Length == 0 ? "NoRows" : "ReportReady");
    });

    public byte[]? CreateCsv()
    {
        if (!CanExport || _result is not { } result)
            return null;
        var text = Translation.Current;
        var separator = text.Culture.TextInfo.ListSeparator;
        var csv = new StringBuilder();
        csv.AppendJoin(separator, result.Columns.Select(column =>
            Escape(column.TitleKey is { } key ? text[key] : column.Name, protect: true))).Append("\r\n");
        foreach (var row in Rows)
            csv.AppendJoin(separator, result.Columns.Select((column,index) =>
                Escape(row[index], protect: column.Kind != "number"))).Append("\r\n");
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(csv.ToString())];
    }

    private static string Escape(string value, bool protect)
    {
        var prefix = value.AsSpan().TrimStart();
        if (protect && (value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n') ||
            (prefix.Length > 0 && prefix[0] is '=' or '+' or '-' or '@')))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    public void ShowFileError()
    {
        _notice = new("ExportFileError");
        Notify(nameof(Status));
    }

    private async Task ExecuteAsync(Func<Task> action)
    {
        if (!IsReady || _disposed)
            return;
        _busy = true;
        _notice = new("AuthWorking");
        RefreshState();
        try
        {
            await action();
            if (_notice?.Key == "AuthWorking")
                _notice = null;
        }
        catch (AuthApiException error)
        {
            if (error.Notice.Key == "SessionExpired" && !_disposed)
                _sessionEnded(error.Notice);
            else
                _notice = error.Notice;
        }
        finally
        {
            _busy = false;
            RefreshState();
        }
    }

    private void ShowResult(ReportResult? result)
    {
        _result = result;
        _rows = result?.Rows.Select(row => new DocumentRow(row, result.Columns)).ToArray() ?? [];
        Notify(nameof(Columns));
        Notify(nameof(Rows));
        Notify(nameof(CanExport));
    }

    private void RefreshState()
    {
        Notify(nameof(IsReady));
        Notify(nameof(CanExport));
        Notify(nameof(Status));
    }

    private void LanguageChanged(object? sender, EventArgs e)
    {
        foreach (var row in Rows)
            row.Translate();
        Notify(nameof(Status));
    }

    public void Dispose()
    {
        _disposed = true;
        Translation.Current.LanguageChanged -= LanguageChanged;
    }
}

public sealed class DocumentRow(string?[] cells, ReportColumn[] columns) : ViewModel
{
    public string this[int index]
    {
        get
        {
            if (cells[index] is not { } value)
                return "";
            var text = Translation.Current;
            if (columns[index].Choices?.FirstOrDefault(choice => choice.Value == value) is { } translated)
                return text[translated.TitleKey];
            return columns[index].Kind switch
            {
                "date" when DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    => date.ToString("d", text.Culture),
                "number" when decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                    => number.ToString(text.Culture),
                "boolean" => text[value == "true" ? "Yes" : "No"],
                _ => value
            };
        }
    }

    public void Translate() => Notify("Item[]");
}
