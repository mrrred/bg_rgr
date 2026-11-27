using BD_RGR.Api.Data;
using Npgsql;

namespace BD_RGR.Api.Reports;

internal sealed record SelectRequest(string? Sql);

internal static class SelectEndpoints
{
    internal static void MapSelectQueries(this WebApplication app)
    {
        app.MapPost("/queries/select", (HttpContext context, DataEndpoints authorization, ReportingDatabase database,
            SelectRequest request) => authorization.RunAsync(context, (role, ct) => ExecuteAsync(database, role, request, ct)));
    }

    private static async Task<IResult> ExecuteAsync(ReportingDatabase database, string role, SelectRequest request,
        CancellationToken ct)
    {
        var query = ManualSelect.Parse(request.Sql, role);
        await using var connection = await database.OpenAsync(ct);
        try
        {
            return Results.Ok(await SelectQueries.RunAsync(connection, query, ct));
        }
        catch (PostgresException error) when (error.SqlState is "57014" or "55P03")
        {
            throw new DataFailure("ReportTimeout", 408);
        }
        catch (PostgresException error) when ((error.SqlState.StartsWith("22", StringComparison.Ordinal) ||
            error.SqlState.StartsWith("42", StringComparison.Ordinal)) && error.SqlState != "42501")
        {
            throw new DataFailure("ReportInvalidSql");
        }
    }
}
