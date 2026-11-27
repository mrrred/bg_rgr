using BD_RGR.Api.Auth;
using Npgsql;
using Microsoft.AspNetCore.Mvc;

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

    public IResult Catalog(string role) => Results.Ok(ResourceCatalog.Resources.Values
        .Where(t => ResourceCatalog.Can(role, t.Name, 'R'))
        .Select(t => new
        {
            t.Name, t.TitleKey, t.IsVehicleSubtype,
            passportFieldCount = t.IsVehicleSubtype ? ResourceCatalog.Vehicle.Fields.Length : 0,
            rights = ResourceCatalog.Rights(role, t.Name),
            queryFields = t.ReadFields.Select(field => field.Name).Prepend("id"),
            fields = t.DisplayFields.Select(f => new
            {
                f.Name, f.TitleKey, f.Kind, f.Nullable, f.MaxLength, f.Target, f.Default,
                choices = f.Choices?.Select(c => new { c.Value, c.TitleKey })
            })
        }));

    public async Task<IResult> ListAsync(string role, string resource, int? id, string? q, string? sort,
        bool? desc, int? page, string? relatedField, int? relatedId, int? pageSize, string? fields,
        bool? labels, int? afterId, string? latestBy, string[] filters, CancellationToken ct)
    {
        var table = Check(role, resource, 'R');
        if (id is <= 0)
            throw new DataFailure("InvalidField");
        var read = ResourceRead.Parse(table, page, pageSize, fields, labels, sort, desc, afterId, latestBy, filters);

        Field? filter = null;
        Resource? related = null;
        if (relatedField is not null || relatedId is not null)
        {
            filter = table.DisplayFields.SingleOrDefault(f => f.Name == relatedField && f.Target is not null);
            if (filter is null || relatedId is not > 0)
                throw new DataFailure("InvalidField");
            related = Check(role, filter.Target!, 'R');
        }

        await using var db = await database.OpenAsync(role, ct);
        if (related is not null && !await TableQueries.ExistsAsync(db, null, related, relatedId!.Value, ct))
            throw new DataFailure("RowNotFound", 404);

        var data = await TableQueries.ListAsync(db, table, read, Query(q), id, filter, relatedId, ct);
        if (id is null)
            return Results.Ok(data);
        return data.Rows.SingleOrDefault() is { } row ? Results.Ok(row) : Error("RowNotFound", 404);
    }

    public async Task<IResult> LookupAsync(string role, string resource, string field, string? q, int? selected, CancellationToken ct)
    {
        var table = Check(role, resource, 'R');
        var reference = table.DisplayFields.SingleOrDefault(f => f.Name == field && f.Target is not null)
            ?? throw new DataFailure("InvalidField");
        var lookup = Check(role, reference.Target!, 'R');
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

    private static Resource Check(string role, string name, char operation)
    {
        if ((name == "vehicle" && operation != 'R') || !ResourceCatalog.Resources.TryGetValue(name, out var table))
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
            string? sort, bool? desc, int? page, string? relatedField, int? relatedId, int? pageSize, string? fields,
            bool? labels, int? afterId, string? latestBy, [FromQuery(Name = "filter")] string[] filters) =>
            api.RunAsync(ctx, (role, ct) => api.ListAsync(role, resource, null, q, sort, desc, page,
                relatedField, relatedId, pageSize, fields, labels, afterId, latestBy, filters, ct)));
        app.MapGet("/tables/{resource}/{id:int}", (HttpContext ctx, DataEndpoints api, string resource, int id) =>
            api.RunAsync(ctx, (role, ct) => api.ListAsync(role, resource, id, null, null, null, null, null, null, null, null, null, null, null, [], ct)));
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
    }
}
