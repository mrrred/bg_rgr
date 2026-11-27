using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace BD_RGR.Api.Data;

internal static class TableWrites
{
    private static readonly string[] FunctionFields =
    [
        "color", "engine_number", "registration_number", "body_number", "chassis_number",
        "manufacture_date", "mileage", "inventory_number", "last_overhaul_date", "status", "model_id", "garage_id"
    ];

    public static async Task<int> SaveAsync(NpgsqlConnection connection, Resource table, string role,
        int? id, Dictionary<string, string?> input, CancellationToken ct)
    {
        var fields = table.DisplayFields.ToArray();
        if (input.Keys.Any(key => !fields.Any(f => f.Name == key)))
            throw new DataFailure("InvalidField");

        var values = fields.ToDictionary(field => field.Name, field =>
        {
            var value = input.TryGetValue(field.Name, out var entered) ? entered : id is null ? field.Default : null;
            return Validate(field, value);
        });

        await using var transaction = await connection.BeginTransactionAsync(ct);
        foreach (var field in fields.Where(f => f.Target is not null))
        {
            if (values[field.Name] is not { } value)
                continue;
            if (!ResourceCatalog.Can(role, field.Target!, 'R') ||
                !await TableQueries.ExistsAsync(connection, transaction, ResourceCatalog.Resources[field.Target!],
                    int.Parse(value, CultureInfo.InvariantCulture), ct))
            {
                throw new DataFailure("InvalidReference", 400, field.Name);
            }
        }

        int savedId;
        if (!table.IsVehicleSubtype)
        {
            savedId = await WriteRowAsync(connection, transaction, table, id, values, null, ct);
        }
        else if (id is { } subtypeId)
        {
            var parentId = await LockVehicleAsync(connection, transaction, table, subtypeId, role, ct);
            await WriteRowAsync(connection, transaction, ResourceCatalog.Vehicle, parentId, values, null, ct);
            savedId = await WriteRowAsync(connection, transaction, table, subtypeId, values, null, ct);
        }
        else if (role is "pass_dispatcher" or "cargo_dispatcher")
        {
            savedId = await CreateThroughFunctionAsync(connection, transaction, table, values, ct);
        }
        else
        {
            var parentId = await WriteRowAsync(connection, transaction, ResourceCatalog.Vehicle, null, values, null, ct);
            savedId = await WriteRowAsync(connection, transaction, table, null, values, parentId, ct);
        }

        await transaction.CommitAsync(ct);
        return savedId;
    }

    public static async Task DeleteAsync(NpgsqlConnection connection, Resource table, int id, string role, CancellationToken ct)
    {
        await using var transaction = await connection.BeginTransactionAsync(ct);
        var name = table.Name;
        if (table.IsVehicleSubtype)
        {
            id = await LockVehicleAsync(connection, transaction, table, id, role, ct);
            name = "vehicle";
        }
        await using var command = new NpgsqlCommand($"DELETE FROM public.{name} WHERE id = $1", connection, transaction);
        command.Parameters.AddWithValue(id);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new DataFailure("RowNotFound", 404);

        await transaction.CommitAsync(ct);
    }

    private static async Task<int> LockVehicleAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Resource table, int subtypeId, string role, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand(
            $"SELECT v.id FROM {TableQueries.Source(table)} WHERE t.id=$1 FOR UPDATE OF v, t", connection, transaction);
        command.Parameters.AddWithValue(subtypeId);
        if (await command.ExecuteScalarAsync(ct) is not int parentId)
            throw new DataFailure("RowNotFound", 404);

        if (role is "director" or "garage_engineer")
        {
            await using var count = new NpgsqlCommand("""
                SELECT (SELECT count(*) FROM public.truck WHERE vehicle_id=$1)
                     + (SELECT count(*) FROM public.passenger_vehicle WHERE vehicle_id=$1)
                     + (SELECT count(*) FROM public.auxiliary_vehicle WHERE vehicle_id=$1)
                """, connection, transaction);
            count.Parameters.AddWithValue(parentId);
            if (await count.ExecuteScalarAsync(ct) is not 1L)
                throw new DataFailure("AmbiguousVehicle", 409);
        }
        return parentId;
    }

    private static async Task<int> WriteRowAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Resource table, int? id, Dictionary<string, string?> values, int? parent, CancellationToken ct)
    {
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        var names = table.Fields.Select(f => f.Name).ToList();
        var args = table.Fields.Select(f => Parameter(command, f, values[f.Name])).ToList();
        if (parent is { } vehicleId)
        {
            names.Add("vehicle_id");
            args.Add("@vehicleId");
            command.Parameters.AddWithValue("vehicleId", vehicleId);
        }
        if (id is { } rowId)
        {
            var assignments = string.Join(", ", names.Zip(args, (name, arg) => $"{name}={arg}"));
            command.CommandText = $"UPDATE public.{table.Name} SET {assignments} WHERE id=@id RETURNING id";
            command.Parameters.AddWithValue("id", rowId);
        }
        else
        {
            command.CommandText = $"INSERT INTO public.{table.Name} ({string.Join(", ", names)}) VALUES ({string.Join(", ", args)}) RETURNING id";
        }
        return await command.ExecuteScalarAsync(ct) is int result ? result : throw new DataFailure("RowNotFound", 404);
    }

    private static async Task<int> CreateThroughFunctionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        Resource table, Dictionary<string, string?> values, CancellationToken ct)
    {
        var source = table.DisplayFields.ToDictionary(f => f.Name);
        await using var command = new NpgsqlCommand { Connection = connection, Transaction = transaction };
        var parameters = FunctionFields.Concat(table.Fields.Select(f => f.Name)).Select(name =>
        {
            var field = source[name];
            var cast = field.Kind == "enum" ? "text" : field.Kind == "text" ? "varchar" : field.Cast;
            return Parameter(command, field, values[name], cast);
        });
        var function = table.Name == "truck" ? "create_freight_vehicle" : "create_passenger_vehicle";
        command.CommandText = $"SELECT public.{function}({string.Join(", ", parameters)})";
        var parentId = (int)(await command.ExecuteScalarAsync(ct))!;
        await using var lookup = new NpgsqlCommand(
            $"SELECT t.id FROM {TableQueries.Source(table)} WHERE t.vehicle_id=$1", connection, transaction);
        lookup.Parameters.AddWithValue(parentId);
        return await lookup.ExecuteScalarAsync(ct) is int id ? id : throw new DataFailure("RowNotFound", 404);
    }

    private static string Parameter(NpgsqlCommand command, Field field, string? value, string? cast = null)
    {
        var name = $"p{command.Parameters.Count}";
        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, (object?)value ?? DBNull.Value);
        return $"@{name}::{cast ?? field.Cast}";
    }

    internal static string? Validate(Field field, string? input)
    {
        var value = input?.Trim();
        if (string.IsNullOrEmpty(value))
        {
            if (field.Nullable)
                return null;
            throw new DataFailure("RequiredField", 400, field.Name);
        }
        if (value.Contains('\0') || (field.MaxLength > 0 && value.EnumerateRunes().Count() > field.MaxLength))
            throw new DataFailure("InvalidField", 400, field.Name);

        var valid = field.Kind switch
        {
            "reference" => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var key) && key > 0,
            "integer" => int.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out _),
            "numeric" => decimal.TryParse(value, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture, out _),
            "date" => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _),
            "boolean" => value is "true" or "false",
            "enum" => field.Choices!.Any(choice => choice.Value == value),
            _ => true
        };
        return valid ? value : throw new DataFailure("InvalidField", 400, field.Name);
    }
}
