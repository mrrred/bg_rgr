using System.Globalization;
using BD_RGR.Api.Data;
using Npgsql;

namespace BD_RGR.Api.Reports;

internal sealed record QueryColumn(string Name, string Kind, string DatabaseType);
internal sealed record QueryResult(QueryColumn[] Columns, List<string?[]> Rows, bool Truncated);

internal static class SelectQueries
{
    internal const int MaxRows = 500;

    internal static async Task<QueryResult> RunAsync(NpgsqlConnection connection, ReportQuery query, CancellationToken ct)
    {
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using (var settings = new NpgsqlBatch(connection, tx))
        {
            foreach (var sql in new[]
            {
                "SET TRANSACTION READ ONLY", "SET LOCAL statement_timeout = '4s'", "SET LOCAL lock_timeout = '2s'",
                "SET LOCAL search_path = pg_catalog", "SET LOCAL DateStyle = 'ISO, YMD'"
            })
                settings.BatchCommands.Add(new NpgsqlBatchCommand(sql));
            await settings.ExecuteNonQueryAsync(ct);
        }
        await using var command = new NpgsqlCommand(query.Sql, connection, tx) { CommandTimeout = 6 };
        command.Parameters.AddRange(query.Parameters.ToArray());
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (reader.FieldCount > 32)
            throw new DataFailure("ReportTooManyColumns");

        var columns = new QueryColumn[reader.FieldCount];
        for (var index = 0; index < columns.Length; index++)
        {
            var type = reader.GetDataTypeName(index);
            var kind = type switch
            {
                "date" => "date",
                "boolean" => "boolean",
                "numeric" or "decimal" or "integer" or "bigint" or "smallint" or "real" or "double precision" => "number",
                _ => "text"
            };
            columns[index] = new(reader.GetName(index), kind, type[(type.LastIndexOf('.') + 1)..]);
        }

        var rows = new List<string?[]>();
        while (rows.Count <= MaxRows && await reader.ReadAsync(ct))
        {
            var row = new string?[columns.Length];
            for (var index = 0; index < row.Length; index++)
                if (!reader.IsDBNull(index))
                    row[index] = Canonical(reader.GetValue(index));
            rows.Add(row);
        }
        var truncated = rows.Count > MaxRows;
        if (truncated)
            rows.RemoveAt(MaxRows);
        return new(columns, rows, truncated);
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
