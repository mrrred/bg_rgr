using System.Collections.Concurrent;
using Npgsql;

namespace BD_RGR.Api.Data;

internal sealed class BusinessDatabase(IConfiguration configuration) : IAsyncDisposable
{
    private static readonly Dictionary<string, string> Groups = new()
    {
        ["director"] = "director_role",
        ["pass_dispatcher"] = "pass_dispatcher_role",
        ["cargo_dispatcher"] = "cargo_dispatcher_role",
        ["garage_engineer"] = "garage_engineer_role",
        ["reader"] = "read_only_role"
    };

    private readonly ConcurrentDictionary<string, Lazy<NpgsqlDataSource>> _sources = new();

    public async Task<NpgsqlConnection> OpenAsync(string role, CancellationToken ct)
    {
        var source = _sources.GetOrAdd(role, code => new(() => CreateSource(code))).Value;
        var connection = await source.OpenConnectionAsync(ct);
        try
        {
            await using var check = new NpgsqlCommand("""
                SELECT pg_has_role(session_user, $1::name, 'USAGE')
                    AND NOT (r.rolsuper OR r.rolbypassrls)
                    AND NOT EXISTS (
                        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
                        WHERE n.nspname = 'public' AND c.relkind IN ('r','p')
                            AND pg_has_role(session_user, c.relowner, 'USAGE')
                    )
                FROM pg_roles r WHERE r.rolname = session_user
                """, connection);
            check.Parameters.AddWithValue(Groups[role]);
            if (await check.ExecuteScalarAsync(ct) is not true)
                throw new DataFailure("BusinessRoleMismatch", 503, $"Database:BusinessRoles:{role}:Username");
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private NpgsqlDataSource CreateSource(string role)
    {
        var prefix = $"Database:BusinessRoles:{role}:";
        if (!int.TryParse(configuration["Database:Port"], out var port) || port is < 1 or > 65535)
            throw new DataFailure("DatabaseConfiguration", 503, "Database:Port");

        var options = new NpgsqlConnectionStringBuilder
        {
            Host = Required("Database:Host"), Port = port, Database = Required("Database:Name"),
            Username = Required(prefix + "Username"), Password = Required(prefix + "Password"),
            Timeout = 5, CommandTimeout = 12
        };
        return NpgsqlDataSource.Create(options.ConnectionString);

        string Required(string field) => configuration[field] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new DataFailure("DatabaseConfiguration", 503, field);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var source in _sources.Values)
            if (source.IsValueCreated)
                await source.Value.DisposeAsync();
    }
}

internal sealed class DataFailure(string code, int status = 400, string? field = null) : Exception
{
    public string Code { get; } = code;
    public int Status { get; } = status;
    public string? Field { get; } = field;
}
