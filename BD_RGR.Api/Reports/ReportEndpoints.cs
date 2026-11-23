using BD_RGR.Api.Auth;
using BD_RGR.Api.Data;
using Npgsql;

namespace BD_RGR.Api.Reports;

internal sealed class ReportEndpoints(BusinessDatabase business, ReportingDatabase reporting, ReportResults results, CsvReport csv)
{
    public async Task<IResult> RunAsync(HttpContext context, string role, string name, ReportRequest request, CancellationToken ct)
    {
        var manual = name == "manual";
        ReportQuery query;
        if (manual)
        {
            query = ManualSelect.Parse(request.Sql, role);
        }
        else
        {
            var template = ReportQueries.Templates.SingleOrDefault(t => t.Name == name)
                ?? throw new DataFailure("RowNotFound", 404);
            if (!ResourceCatalog.Can(role, template.Source, 'R'))
                throw new DataFailure("OperationDenied", 403);
            query = new(template.Sql + $" LIMIT {ReportQueries.MaxRows + 1}", []);
        }

        await using var db = manual ? await reporting.OpenAsync(ct) : await business.OpenAsync(role, ct);
        ReportResult result;
        try
        {
            result = await ReportQueries.RunAsync(db, query, ct);
        }
        catch (PostgresException e) when (e.SqlState is "57014" or "55P03")
        {
            throw new DataFailure("ReportTimeout", 408);
        }
        catch (PostgresException e) when (manual &&
            (e.SqlState.StartsWith("22", StringComparison.Ordinal) || e.SqlState.StartsWith("42", StringComparison.Ordinal))
            && e.SqlState != "42501")
        {
            throw new DataFailure("ReportInvalidSql");
        }

        results.Store(Owner(context, role), result);
        return Results.Ok(result);
    }

    public IResult Export(HttpContext context, string role, Guid id, string? language)
    {
        var snapshot = results.Find(Owner(context, role), id) ?? throw new DataFailure("ReportResultExpired", 404);
        return Results.File(csv.Encode(snapshot, language == "en" ? "en" : "ru"), "text/csv; charset=utf-8", "report.csv");
    }

    public IResult Metrics(string role) => Results.Ok(ReportQueries.Metrics
        .Where(metric => ResourceCatalog.Can(role, metric.Source, 'R'))
        .Select(metric => new { metric.Name, metric.TitleKey }));

    public async Task<IResult> AnalyticsAsync(string role, string metric, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (from > to || to.DayNumber - from.DayNumber >= 366)
            throw new DataFailure("AnalyticsPeriodInvalid");

        var definition = ReportQueries.Metrics.SingleOrDefault(value => value.Name == metric)
            ?? throw new DataFailure("RowNotFound", 404);
        if (!ResourceCatalog.Can(role, definition.Source, 'R'))
            throw new DataFailure("OperationDenied", 403);

        await using var db = await business.OpenAsync(role, ct);
        await using var tx = await ReportQueries.ReadOnlyAsync(db, ct);
        await using var command = new NpgsqlCommand($"""
            SELECT {definition.DateField}, {definition.Aggregation}
            FROM public.{definition.Source}
            WHERE {definition.DateField} BETWEEN $1 AND $2
            GROUP BY {definition.DateField}
            ORDER BY {definition.DateField}
            """, db, tx) { CommandTimeout = 6 };
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var points = new List<AnalyticsPoint>();
        while (await reader.ReadAsync(ct))
            points.Add(new(reader.GetFieldValue<DateOnly>(0), reader.GetInt64(1)));

        return Results.Ok(new { from, to, points, updatedAt = DateTimeOffset.UtcNow });
    }

    private static ResultOwner Owner(HttpContext context, string role) =>
        new(SessionStore.Fingerprint(AuthEndpoints.BearerToken(context)!), role);

    private sealed record AnalyticsPoint(DateOnly Date, long Value);
}

internal sealed record ReportRequest(string? Sql);

internal static class ReportRouting
{
    internal static void MapReportEndpoints(this WebApplication app)
    {
        app.MapPost("/documents/{template}/run", (HttpContext context, DataEndpoints authorization, ReportEndpoints reports,
            string template, ReportRequest request) =>
            authorization.RunAsync(context, (role, ct) => reports.RunAsync(context, role, template, request, ct)));
        app.MapGet("/documents/results/{id:guid}/export", (HttpContext context, DataEndpoints authorization, ReportEndpoints reports,
            Guid id, string? language) =>
            authorization.RunAsync(context, (role, _) => Task.FromResult(reports.Export(context, role, id, language))));
        app.MapGet("/analytics/metrics", (HttpContext context, DataEndpoints authorization, ReportEndpoints reports) =>
            authorization.RunAsync(context, (role, _) => Task.FromResult(reports.Metrics(role))));
        app.MapGet("/analytics/data", (HttpContext context, DataEndpoints authorization, ReportEndpoints reports,
            string metric, DateOnly from, DateOnly to) =>
            authorization.RunAsync(context, (role, ct) => reports.AnalyticsAsync(role, metric, from, to, ct)));
    }
}
