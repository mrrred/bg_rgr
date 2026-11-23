using System.Net.Http;
using BD_RGR.Models;

namespace BD_RGR.Services;

public sealed partial class AuthApiClient
{
    public async Task<ReportResult> ReportAsync(Session session, string template, string? sql)
    {
        using var response = await SendAsync(HttpMethod.Post, $"documents/{Uri.EscapeDataString(template)}/run",
            new { sql }, session.Token);
        return await ReadAsync<ReportResult>(response);
    }

    public async Task<byte[]> ReportCsvAsync(Session session, Guid id, string language)
    {
        using var response = await SendAsync(HttpMethod.Get,
            $"documents/results/{id}/export?language={Uri.EscapeDataString(language)}", token: session.Token);
        return await response.Content.ReadAsByteArrayAsync();
    }

    public Task<AnalyticsMetric[]> MetricsAsync(Session session) => GetAsync<AnalyticsMetric[]>(session, "analytics/metrics");

    public Task<AnalyticsResult> AnalyticsAsync(Session session, string metric, DateTime from, DateTime to) =>
        GetAsync<AnalyticsResult>(session, FormattableString.Invariant(
            $"analytics/data?metric={Uri.EscapeDataString(metric)}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));
}
