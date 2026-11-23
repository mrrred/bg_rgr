using BD_RGR.Api.Auth;
using Npgsql;

namespace BD_RGR.Api.Data;

internal sealed class DataEndpoints(AccountsDatabase accounts, SessionStore sessions, BusinessDatabase database,
    ILogger<DataEndpoints> logger)
{
    public async Task<IResult> RunAsync(HttpContext context, Func<string, CancellationToken, Task<IResult>> action)
    {
        var databaseError = "DatabaseUnavailable";
        try
        {
            var ct = context.RequestAborted;
            var role = await sessions.ResolveRoleAsync(context, accounts, ct);
            if (role is null)
                throw new DataFailure("SessionExpired", 401);
            databaseError = "BusinessDatabaseUnavailable";
            return await action(role, ct);
        }
        catch (DataFailure e) { return Error(e.Code, e.Status, e.Field); }
        catch (AccountsConfigurationException e) { return Error("DatabaseConfiguration", 503, e.Field); }
        catch (PostgresException e)
        {
            logger.LogWarning("Business request failed with SQLSTATE {State}, constraint {Constraint}.", e.SqlState, e.ConstraintName);
            return e.SqlState switch
            {
                "42501" => Error("OperationDenied", 403),
                "23503" or "23001" when HttpMethods.IsDelete(context.Request.Method) => Error("HasDependencies", 409),
                "23503" or "23001" => Error("InvalidReference", 409),
                "23505" => Error("DuplicateRow", 409, ResourceCatalog.UniqueFields.GetValueOrDefault(e.ConstraintName ?? "")),
                "23514" or "23502" or "22001" or "22003" or "22P02" => Error("ValidationFailed", 400, e.ColumnName),
                "42P01" or "42703" or "42883" or "3F000" when databaseError == "BusinessDatabaseUnavailable"
                    => Error("BusinessSchemaMismatch", 503),
                _ => Error(databaseError, 503)
            };
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            logger.LogWarning("Database connection or command failed ({Code}).", databaseError);
            return Error(databaseError, 503);
        }
    }

    public IResult Catalog(string role) => Results.Ok(ResourceCatalog.Tables
        .Where(t => ResourceCatalog.Can(role, t.Name, 'R'))
        .Select(t => new
        {
            t.Name, t.TitleKey, t.IsVehicleSubtype,
            passportFieldCount = t.IsVehicleSubtype ? ResourceCatalog.Vehicle.Fields.Length : 0,
            rights = ResourceCatalog.Rights(role, t.Name),
            fields = t.DisplayFields.Select(f => new
            {
                f.Name, f.TitleKey, f.Kind, f.Nullable, f.MaxLength, f.Target, f.Default,
                choices = f.Choices?.Select(c => new { c.Value, c.TitleKey })
            })
        }));

    public async Task<IResult> ListAsync(string role, string resource, int? id, string? q, string? sort,
        bool? desc, int? page, string? relatedField, int? relatedId, CancellationToken ct)
    {
        var table = Check(role, resource, 'R');
        if (id is <= 0 || page is < 0 or > 1_000_000 ||
            (sort is not null and not "id" && !table.DisplayFields.Any(f => f.Name == sort)))
            throw new DataFailure("InvalidField");

        Field? filter = null;
        Resource? related = null;
        if (relatedField is not null || relatedId is not null)
        {
            filter = table.DisplayFields.SingleOrDefault(f => f.Name == relatedField && f.Target is not null);
            if (filter is null || relatedId is not > 0)
                throw new DataFailure("InvalidField");
            related = Check(role, filter.Target!, 'R', true);
        }

        await using var db = await database.OpenAsync(role, ct);
        if (related is not null && !await TableQueries.ExistsAsync(db, null, related, relatedId!.Value, ct))
            throw new DataFailure("RowNotFound", 404);

        var data = await TableQueries.ListAsync(db, table, page ?? 0, Query(q), sort, desc ?? false, id, filter, relatedId, ct);
        if (id is null)
            return Results.Ok(data);
        return data.Rows.SingleOrDefault() is { } row ? Results.Ok(row) : Error("RowNotFound", 404);
    }

    public async Task<IResult> LookupAsync(string role, string resource, string field, string? q, int? selected, CancellationToken ct)
    {
        var table = Check(role, resource, 'R');
        var reference = table.DisplayFields.SingleOrDefault(f => f.Name == field && f.Target is not null)
            ?? throw new DataFailure("InvalidField");
        var lookup = Check(role, reference.Target!, 'R', true);
        await using var db = await database.OpenAsync(role, ct);
        return Results.Ok(await TableQueries.LookupAsync(db, lookup, Query(q), selected, ct));
    }

    public async Task<IResult> SaveAsync(string role, string resource, int? id, Dictionary<string, string?> values,
        CancellationToken ct)
    {
        var table = Check(role, resource, id is null ? 'C' : 'U');
        await using var db = await database.OpenAsync(role, ct);
        var savedId = await TableWrites.SaveAsync(db, table, role, id, values, ct);
        return id is null ? Results.Created($"/tables/{resource}/{savedId}", new { id = savedId }) : Results.Ok(new { id = savedId });
    }

    public async Task<IResult> DeleteAsync(string role, string resource, int id, CancellationToken ct)
    {
        var table = Check(role, resource, 'D');
        await using var db = await database.OpenAsync(role, ct);
        await TableWrites.DeleteAsync(db, table, id, role, ct);
        return Results.NoContent();
    }

    public async Task<IResult> VehicleLinkAsync(string role, int id, CancellationToken ct)
    {
        Check(role, "vehicle", 'R', true);
        await using var db = await database.OpenAsync(role, ct);
        var rows = new List<(string Name, int Id)>();
        foreach (var table in ResourceCatalog.Tables.Where(t => t.IsVehicleSubtype && ResourceCatalog.Can(role, t.Name, 'R')))
        {
            await using var command = new NpgsqlCommand(
                $"SELECT t.id FROM {TableQueries.Source(table)} WHERE t.vehicle_id = $1", db);
            command.Parameters.AddWithValue(id);
            if (await command.ExecuteScalarAsync(ct) is int target)
                rows.Add((table.Name, target));
        }
        return rows.Count switch
        {
            1 => Results.Ok(new { resource = rows[0].Name, id = rows[0].Id }),
            0 => Error("RowNotFound", 404),
            _ => Error("AmbiguousVehicle", 409)
        };
    }

    public async Task<IResult> MileageAsync(string role, int vehicleId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        if (role != "garage_engineer")
            throw new DataFailure("OperationDenied", 403);
        if (from > to)
            throw new DataFailure("InvalidPeriod");
        await using var db = await database.OpenAsync(role, ct);
        if (!await TableQueries.ExistsAsync(db, null, ResourceCatalog.Vehicle, vehicleId, ct))
            throw new DataFailure("RowNotFound", 404);
        await using var command = new NpgsqlCommand("SELECT public.calculate_vehicle_mileage($1, $2, $3)", db);
        command.Parameters.AddWithValue(vehicleId);
        command.Parameters.AddWithValue(from);
        command.Parameters.AddWithValue(to);
        return Results.Ok(new { mileage = await command.ExecuteScalarAsync(ct) as int? });
    }

    private static Resource Check(string role, string name, char operation, bool allowVehicle = false)
    {
        if ((!allowVehicle && name == "vehicle") || !ResourceCatalog.Resources.TryGetValue(name, out var table))
            throw new DataFailure("RowNotFound", 404);
        if (!ResourceCatalog.Can(role, name, operation))
            throw new DataFailure("OperationDenied", 403);

        return table;
    }

    private static string Query(string? query) => query is { Length: > 200 }
        ? throw new DataFailure("InvalidField")
        : query?.Trim() ?? "";

    private static IResult Error(string code, int status, string? field = null) =>
        Results.Json(new { code, field }, statusCode: status);
}

internal static class DataRouting
{
    public static void MapDataEndpoints(this WebApplication app)
    {
        app.MapGet("/tables", (HttpContext ctx, DataEndpoints api) =>
            api.RunAsync(ctx, (role, _) => Task.FromResult(api.Catalog(role))));
        app.MapGet("/tables/{resource}", (HttpContext ctx, DataEndpoints api, string resource, string? q,
            string? sort, bool? desc, int? page, string? relatedField, int? relatedId) =>
            api.RunAsync(ctx, (role, ct) => api.ListAsync(role, resource, null, q, sort, desc, page, relatedField, relatedId, ct)));
        app.MapGet("/tables/{resource}/{id:int}", (HttpContext ctx, DataEndpoints api, string resource, int id) =>
            api.RunAsync(ctx, (role, ct) => api.ListAsync(role, resource, id, null, null, null, null, null, null, ct)));
        app.MapGet("/lookups/{resource}/{field}", (HttpContext ctx, DataEndpoints api, string resource,
            string field, string? q, int? selected) =>
            api.RunAsync(ctx, (role, ct) => api.LookupAsync(role, resource, field, q, selected, ct)));
        app.MapPost("/tables/{resource}", (HttpContext ctx, DataEndpoints api, string resource, Dictionary<string, string?> values) =>
            api.RunAsync(ctx, (role, ct) => api.SaveAsync(role, resource, null, values, ct)));
        app.MapPut("/tables/{resource}/{id:int}", (HttpContext ctx, DataEndpoints api, string resource, int id,
            Dictionary<string, string?> values) =>
            api.RunAsync(ctx, (role, ct) => api.SaveAsync(role, resource, id, values, ct)));
        app.MapDelete("/tables/{resource}/{id:int}", (HttpContext ctx, DataEndpoints api, string resource, int id) =>
            api.RunAsync(ctx, (role, ct) => api.DeleteAsync(role, resource, id, ct)));
        app.MapGet("/vehicle-links/{id:int}", (HttpContext ctx, DataEndpoints api, int id) =>
            api.RunAsync(ctx, (role, ct) => api.VehicleLinkAsync(role, id, ct)));
        app.MapGet("/mileage", (HttpContext ctx, DataEndpoints api, int vehicleId, DateOnly from, DateOnly to) =>
            api.RunAsync(ctx, (role, ct) => api.MileageAsync(role, vehicleId, from, to, ct)));
    }
}
