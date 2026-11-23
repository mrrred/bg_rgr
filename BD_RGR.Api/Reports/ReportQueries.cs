using System.Globalization;
using BD_RGR.Api.Data;
using Npgsql;

namespace BD_RGR.Api.Reports;

internal sealed record ReportTemplate(string Name, string TitleKey, string Source, string Sql);
internal sealed record Metric(string Name, string TitleKey, string Source, string DateField, string Aggregation);
internal sealed record ReportOption(string Value, string TitleKey);
internal sealed record ReportColumn(string Name, string? TitleKey, string Kind, ReportOption[]? Choices);
internal sealed record ReportResult(Guid Id, ReportColumn[] Columns, List<string?[]> Rows, bool Truncated);

internal static class ReportQueries
{
    internal const int MaxRows = 500;

    internal static readonly ReportTemplate[] Templates =
    [
        new("routes", "RouteReport", "route", """
            SELECT r.number, r.name, a.name AS start_point, b.name AS end_point
            FROM public.route AS r
            JOIN public.point AS a ON a.id = r.start_point_id
            JOIN public.point AS b ON b.id = r.end_point_id
            ORDER BY r.number, r.id
            """),
        new("cargo", "CargoReport", "cargo_transportation", """
            SELECT c.date, v.registration_number, m.name AS model
            FROM public.cargo_transportation AS c
            JOIN public.truck AS t ON t.id = c.truck_id
            JOIN public.vehicle AS v ON v.id = t.vehicle_id
            JOIN public.model AS m ON m.id = v.model_id
            ORDER BY c.date, c.id
            """),
        new("mileage", "MileageReport", "vehicle_mileage_log", """
            SELECT j.reading_date, v.registration_number, j.mileage
            FROM public.vehicle_mileage_log AS j
            JOIN public.vehicle AS v ON v.id = j.vehicle_id
            ORDER BY j.reading_date, j.id
            """)
    ];

    internal static readonly Metric[] Metrics =
    [
        new("passengers", "PassengersByDay", "trip", "date", "SUM(passenger_count)"),
        new("cargo", "CargoByDay", "cargo_transportation", "date", "COUNT(*)"),
        new("mileage", "ReadingsByDay", "vehicle_mileage_log", "reading_date", "COUNT(*)")
    ];

    private static readonly Dictionary<string, string> HeaderKeys =
        ResourceCatalog.Tables.SelectMany(t => t.DisplayFields)
            .GroupBy(f => f.Name).ToDictionary(g => g.Key, g => g.First().TitleKey);

    static ReportQueries()
    {
        HeaderKeys["start_point"] = "FieldStartPoint";
        HeaderKeys["end_point"] = "FieldEndPoint";
        HeaderKeys["model"] = "Model";
        HeaderKeys["id"] = "FieldId";
        HeaderKeys["count"] = "FieldQuantity";
    }

    internal static async Task<NpgsqlTransaction> ReadOnlyAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        var tx = await connection.BeginTransactionAsync(ct);
        try
        {
            await using var batch = new NpgsqlBatch(connection, tx);
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SET TRANSACTION READ ONLY"));
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SET LOCAL statement_timeout = '4s'"));
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SET LOCAL lock_timeout = '2s'"));
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SET LOCAL search_path = pg_catalog"));
            batch.BatchCommands.Add(new NpgsqlBatchCommand("SET LOCAL DateStyle = 'ISO, YMD'"));
            await batch.ExecuteNonQueryAsync(ct);
            return tx;
        }
        catch
        {
            await tx.DisposeAsync();
            throw;
        }
    }

    internal static async Task<ReportResult> RunAsync(NpgsqlConnection connection, ReportQuery query, CancellationToken ct)
    {
        await using var tx = await ReadOnlyAsync(connection, ct);
        await using var command = new NpgsqlCommand(query.Sql, connection, tx) { CommandTimeout = 6 };
        command.Parameters.AddRange(query.Parameters.ToArray());
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (reader.FieldCount > 32)
            throw new DataFailure("ReportTooManyColumns");

        var columns = new ReportColumn[reader.FieldCount];
        for (var i = 0; i < columns.Length; i++)
        {
            var name = reader.GetName(i);
            var dataType = reader.GetDataTypeName(i);
            var kind = dataType switch
            {
                "date" => "date",
                "boolean" => "boolean",
                "numeric" or "decimal" or "integer" or "bigint" or "smallint" or "real" or "double precision" => "number",
                _ => "text"
            };
            var type = dataType[(dataType.LastIndexOf('.') + 1)..];
            var choices = ResourceCatalog.Enums.GetValueOrDefault(type)?
                .Select(choice => new ReportOption(choice.Value, choice.TitleKey)).ToArray();
            columns[i] = new(name, HeaderKeys.GetValueOrDefault(name), kind, choices);
        }

        var rows = new List<string?[]>();
        while (await reader.ReadAsync(ct) && rows.Count <= MaxRows)
        {
            var row = new string?[columns.Length];
            for (var i = 0; i < row.Length; i++)
                if (!reader.IsDBNull(i))
                    row[i] = Canonical(reader.GetValue(i));
            rows.Add(row);
        }

        var truncated = rows.Count > MaxRows;
        if (truncated)
            rows.RemoveAt(MaxRows);

        return new(Guid.NewGuid(), columns, rows, truncated);
    }

    private static string Canonical(object value) => value switch
    {
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        bool boolean => boolean ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };
}
