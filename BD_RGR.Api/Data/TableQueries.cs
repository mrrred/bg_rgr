using Npgsql;
using NpgsqlTypes;

namespace BD_RGR.Api.Data;

internal sealed record TableRow(int Id, Dictionary<string, string?> Values, Dictionary<string, string?> Labels);
internal sealed record LookupValue(int Id, string Label);
internal sealed record TablePage(bool HasMore, List<TableRow> Rows);

internal static class TableQueries
{
    private const int LookupLimit = 50;

    public static async Task<TablePage> ListAsync(NpgsqlConnection connection, Resource table, ResourceRead query,
        string search, int? id, Field? relatedField, int? relatedId, CancellationToken ct)
    {
        var fields = query.Fields;
        var data = fields.Select(f => Column(table, f)).ToArray();
        var from = Source(table);
        var labels = new Dictionary<Field, string>();
        var labelFields = search.Length > 0 ? table.DisplayFields : query.Labels
            ? fields.Concat(query.Sort is { } sort ? [sort] : []) : [];
        foreach (var field in labelFields.Where(field => field.Target is not null).Distinct())
        {
            var target = field.Target!;
            var reference = ResourceCatalog.Resources[target];
            var alias = $"ref{labels.Count}";
            var vehicleAlias = $"ref_vehicle{labels.Count}";
            from += $" LEFT JOIN public.{target} {alias} ON {alias}.id = {Column(table, field)}";
            if (reference.IsVehicleSubtype)
                from += $" LEFT JOIN public.vehicle {vehicleAlias} ON {vehicleAlias}.id = {alias}.vehicle_id";

            var visibleAlias = reference.IsVehicleSubtype ? vehicleAlias : alias;
            labels[field] = $"CASE WHEN {visibleAlias}.id IS NOT NULL THEN {Label(reference, alias, vehicleAlias)} END";
        }
        string Display(Field field) => labels.GetValueOrDefault(field) ?? Column(table, field);
        var order = query.Sort is { } sortBy ? query.Labels ? Display(sortBy) : Column(table, sortBy) : "t.id";
        if (query.Sort?.Kind == "text" || (query.Labels && query.Sort?.Kind == "reference"))
            order = $"({order}) COLLATE pg_catalog.\"unicode\"";
        var references = query.Labels ? fields.Where(labels.ContainsKey).ToArray() : [];
        var select = string.Join(", ", data.Select((column, index) => $"({column})::text AS value{index}")
            .Concat(references.Select((field, index) => $"{labels[field]} AS label{index}")).Prepend("t.id AS row_id"));
        var conditions = new List<string>();
        await using var command = new NpgsqlCommand { Connection = connection };
        if (id is { } rowId)
        {
            conditions.Add("t.id = @id");
            command.Parameters.AddWithValue("id", rowId);
        }
        if (relatedField is not null && relatedId is { } filterId)
        {
            conditions.Add($"{Column(table, relatedField)} = @related");
            command.Parameters.AddWithValue("related", filterId);
        }
        if (search.Length > 0)
        {
            var searchColumns = table.DisplayFields.Select(field => SearchExpression(field, Display(field))).Prepend("t.id::text");
            conditions.Add($"strpos(lower(concat_ws(' ', {string.Join(", ", searchColumns)}) COLLATE pg_catalog.\"unicode\"), lower(@search COLLATE pg_catalog.\"unicode\")) > 0");
            command.Parameters.AddWithValue("search", search);
        }
        for (var i = 0; i < query.Filters.Length; i++)
            conditions.Add(ResourceRead.Condition(command, table, query.Filters[i], i));
        if (query.AfterId is { } afterId)
        {
            conditions.Add("t.id > @afterId");
            command.Parameters.AddWithValue("afterId", afterId);
        }
        var where = conditions.Count == 0 ? "" : " WHERE " + string.Join(" AND ", conditions);
        var direction = query.Descending ? "DESC" : "ASC";
        var sql = $"SELECT {select} FROM {from}{where} ORDER BY {order} {direction} NULLS LAST, t.id";
        if (query.LatestBy is { } latestBy)
        {
            var group = Column(table, latestBy);
            sql = $"""
                SELECT * FROM (
                    SELECT DISTINCT ON ({group}) {select}, {order} AS sort_value FROM {from}{where}
                    ORDER BY {group}, {order} {direction} NULLS LAST, t.id DESC
                ) AS grouped ORDER BY sort_value {direction} NULLS LAST, row_id
                """;
        }
        if (query.Limit is { } limit)
        {
            sql += " LIMIT @limit";
            command.Parameters.AddWithValue("limit", limit + 1);
        }
        if (query.Offset is { } offset)
        {
            sql += " OFFSET @offset";
            command.Parameters.AddWithValue("offset", offset);
        }
        command.CommandText = sql;

        var rows = new List<TableRow>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var values = new Dictionary<string, string?>();
            var links = new Dictionary<string, string?>();
            for (var i = 0; i < fields.Length; i++)
                values[fields[i].Name] = reader.IsDBNull(i + 1) ? null : reader.GetString(i + 1);
            for (var i = 0; i < references.Length; i++)
            {
                var index = 1 + fields.Length + i;
                links[references[i].Name] = reader.IsDBNull(index) ? null : reader.GetString(index);
            }
            rows.Add(new(reader.GetInt32(0), values, links));
        }
        if (query.Limit is { } maxRows && rows.Count > maxRows)
        {
            rows.RemoveAt(maxRows);
            return new(true, rows);
        }
        return new(false, rows);
    }

    public static async Task<List<LookupValue>> LookupAsync(NpgsqlConnection connection, Resource target, string query,
        int? selected, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"""
            SELECT id, label FROM (SELECT t.id, {Label(target, "t", "v")} AS label FROM {Source(target)}) AS choices
            WHERE id = @selected OR strpos(lower(label COLLATE pg_catalog."unicode"), lower(@query COLLATE pg_catalog."unicode")) > 0
            ORDER BY (id IS NOT DISTINCT FROM @selected) DESC, label COLLATE pg_catalog."unicode", id LIMIT {LookupLimit}
            """, connection);
        command.Parameters.AddWithValue("query", query);
        command.Parameters.AddWithValue("selected", NpgsqlDbType.Integer, (object?)selected ?? DBNull.Value);
        var rows = new List<LookupValue>();
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            rows.Add(new(reader.GetInt32(0), reader.GetString(1)));
        return rows;
    }

    public static async Task<bool> ExistsAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction,
        Resource target, int id, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand($"SELECT EXISTS (SELECT 1 FROM {Source(target)} WHERE t.id = $1)",
            connection, transaction);
        command.Parameters.AddWithValue(id);
        return await command.ExecuteScalarAsync(ct) is true;
    }

    internal static string Source(Resource resource) => resource.IsVehicleSubtype
        ? $"public.{resource.Name} t JOIN public.vehicle v ON v.id = t.vehicle_id"
        : $"public.{resource.Name} t";

    internal static string Column(Resource table, Field field) =>
        $"{(table.IsVehicleSubtype && ResourceCatalog.Vehicle.Fields.Contains(field) ? "v" : "t")}.{field.Name}";

    private static string Label(Resource target, string a, string vehicle)
    {
        if (target.IsVehicleSubtype)
            return VehicleLabel(vehicle);
        return target.Name switch
        {
            "employee" => $"concat_ws(' ', {a}.last_name, {a}.first_name, {a}.middle_name) || ' · #' || {a}.id",
            "vehicle" => VehicleLabel(a),
            "model" => ModelLabel(a),
            "garage" or "route" or "brigade" => $"concat_ws(' · ', {a}.number, {a}.name)",
            "job_position" => $"concat_ws(' · ', {a}.okpdtr_2025_code, {a}.name)",
            "cargo" => $"concat_ws(' · ', {a}.name, (SELECT k.name FROM public.cargo_type k WHERE k.id = {a}.cargo_type_id))",
            "cargo_transportation" => $"concat_ws(' · ', {a}.date, (SELECT cv.registration_number FROM public.truck ct JOIN public.vehicle cv ON cv.id=ct.vehicle_id WHERE ct.id={a}.truck_id), '#' || {a}.id)",
            "repair" => $"concat_ws(' · ', {a}.date, (SELECT rv2.registration_number FROM public.vehicle rv2 WHERE rv2.id={a}.vehicle_id), '#' || {a}.id)",
            _ => $"{a}.name::text"
        };
    }

    private static string ModelLabel(string a) =>
        $"concat_ws(' ', (SELECT mb.name FROM public.brand mb WHERE mb.id = {a}.brand_id), {a}.name)";

    private static string VehicleLabel(string a) =>
        $"concat_ws(' · ', {a}.registration_number, (SELECT {ModelLabel("vm")} FROM public.model vm WHERE vm.id = {a}.model_id))";

    private static string SearchExpression(Field field, string expression)
    {
        if (field.Choices is { } choices)
        {
            var english = string.Join(" ", choices.Select(c => $"WHEN '{c.Value}' THEN '{c.English}'"));
            return $"concat_ws(' ', ({expression})::text, CASE ({expression})::text {english} END)";
        }
        return field.Kind switch
        {
            "date" => $"concat_ws(' ', {expression}::text, to_char({expression}, 'DD.MM.YYYY'), to_char({expression}, 'FMMM/FMDD/YYYY'))",
            "numeric" => $"concat_ws(' ', {expression}::text, replace({expression}::text, '.', ','))",
            "boolean" => $"CASE WHEN {expression} THEN 'true yes да' ELSE 'false no нет' END",
            _ => $"({expression})::text"
        };
    }
}
