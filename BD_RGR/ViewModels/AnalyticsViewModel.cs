using System.Windows.Threading;
using BD_RGR.Models;
using BD_RGR.Services;

namespace BD_RGR.ViewModels;

public sealed class AnalyticsViewModel : ViewModel, IDisposable
{
    private readonly AuthApiClient _api;
    private readonly Session _session;
    private readonly Action<AuthNotice> _sessionEnded;
    private readonly DispatcherTimer _timer;
    private AnalyticsMetric[] _metrics = [];
    private AnalyticsMetric? _selected;
    private DateTime? _from = DateTime.Today.AddDays(-29);
    private DateTime? _to = DateTime.Today;
    private ChartSnapshot? _snapshot;
    private AuthNotice? _notice;
    private bool _busy;
    private bool _disposed;

    public AnalyticsViewModel(AuthApiClient api, Session session, Action<AuthNotice> sessionEnded)
    {
        _api = api;
        _session = session;
        _sessionEnded = sessionEnded;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
        _timer.Tick += OnTick;
        Translation.Current.LanguageChanged += OnLanguageChanged;
    }

    public AnalyticsMetric[] Metrics => _metrics;
    public AnalyticsMetric? Selected { get => _selected; set { if (Set(ref _selected, value)) ClearSelection(); } }
    public DateTime? From { get => _from; set { if (Set(ref _from, value)) ClearSelection(); } }
    public DateTime? To { get => _to; set { if (Set(ref _to, value)) ClearSelection(); } }
    public ChartSnapshot? Snapshot => _snapshot;
    public bool IsReady => !_busy;
    public bool CanExport => Snapshot is { Result.Points.Length: > 0 };
    public string ChartTitle => Snapshot is { } data ? Translation.Current[data.Metric.TitleKey] : "";
    public string ChartPeriod
    {
        get
        {
            if (Snapshot is not { } chart)
                return "";
            var culture = Translation.Current.Culture;
            return $"{chart.Result.From.ToString("d", culture)} – {chart.Result.To.ToString("d", culture)}";
        }
    }

    public string Status
    {
        get
        {
            var text = Translation.Current;
            if (_notice is { } notice)
                return notice.Field is { } field ? string.Format(text.Culture, text[notice.Key], field) : text[notice.Key];
            if (Snapshot is not { } snapshot)
                return "";
            var updated = string.Format(text.Culture, text["AnalyticsUpdated"], snapshot.Result.UpdatedAt.LocalDateTime.ToString("T", text.Culture));
            return snapshot.Result.Points.Length == 0 ? $"{text["AnalyticsEmpty"]} · {updated}" : updated;
        }
    }

    public async Task StartAsync()
    {
        _timer.Start();
        await RefreshAsync();
    }

    public void Stop() => _timer.Stop();

    public async Task RefreshAsync()
    {
        if (_disposed || _busy)
            return;
        if (_metrics.Length > 0 && !ValidPeriod())
        {
            _notice = new("AnalyticsPeriodInvalid");
            Notify(nameof(Status));
            return;
        }

        _busy = true;
        Notify(nameof(IsReady));
        if (Snapshot is null)
        {
            _notice = new("AuthWorking");
            Notify(nameof(Status));
        }
        try
        {
            if (_metrics.Length == 0)
            {
                _metrics = await _api.MetricsAsync(_session);
                Notify(nameof(Metrics));
                Selected = _metrics.FirstOrDefault();
            }
            if (Selected is not { } metric || !ValidPeriod())
                throw new AuthApiException("AnalyticsPeriodInvalid");

            var from = From!.Value.Date;
            var to = To!.Value.Date;
            var result = await _api.AnalyticsAsync(_session, metric.Name, from, to);
            if (!_disposed && Selected == metric && From?.Date == from && To?.Date == to)
            {
                _snapshot = new(metric, result);
                _notice = null;
                RefreshDisplay();
            }
        }
        catch (AuthApiException error)
        {
            if (error.Notice.Key == "SessionExpired" && !_disposed)
            {
                _timer.Stop();
                _sessionEnded(error.Notice);
            }
            else
                _notice = error.Notice;
        }
        finally
        {
            _busy = false;
            Notify(nameof(IsReady));
            Notify(nameof(Status));
        }
    }

    public void ShowFileError()
    {
        _notice = new("ExportFileError");
        Notify(nameof(Status));
    }

    private void ClearSelection()
    {
        _snapshot = null;
        _notice = null;
        RefreshDisplay();
    }

    private bool ValidPeriod() => From is { } from && To is { } to && from.Date <= to.Date && (to.Date - from.Date).Days < 366;

    private void RefreshDisplay()
    {
        Notify(nameof(Snapshot));
        Notify(nameof(CanExport));
        Notify(nameof(ChartTitle));
        Notify(nameof(ChartPeriod));
        Notify(nameof(Status));
    }

    private async void OnTick(object? sender, EventArgs e) => await RefreshAsync();
    private void OnLanguageChanged(object? sender, EventArgs e) => RefreshDisplay();

    public void Dispose()
    {
        _disposed = true;
        _timer.Stop();
        _timer.Tick -= OnTick;
        Translation.Current.LanguageChanged -= OnLanguageChanged;
    }
}
