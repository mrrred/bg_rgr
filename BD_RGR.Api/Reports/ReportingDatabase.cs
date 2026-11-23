using BD_RGR.Api.Data;
using Npgsql;

namespace BD_RGR.Api.Reports;

internal sealed class ReportingDatabase(IConfiguration configuration) : IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _source = new(() => Create(configuration));

    public async Task<NpgsqlConnection> OpenAsync(CancellationToken ct)
    {
        var connection = await _source.Value.OpenConnectionAsync(ct);
        try
        {
            await using var check = new NpgsqlCommand("""
                SELECT pg_has_role(session_user, 'app_report_reader', 'USAGE')
                    AND NOT (r.rolsuper OR r.rolbypassrls OR r.rolcreaterole OR r.rolcreatedb)
                    AND NOT EXISTS (SELECT 1 FROM pg_namespace n WHERE n.nspname='app'
                        AND has_schema_privilege(n.oid, 'USAGE'))
                    AND NOT EXISTS (
                        SELECT 1 FROM pg_class c
                        WHERE CASE WHEN c.relkind='S' THEN has_sequence_privilege(c.oid,'USAGE,UPDATE') ELSE false END
                    )
                    AND NOT EXISTS (
                        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
                        WHERE n.nspname <> 'information_schema' AND n.nspname NOT LIKE 'pg_%'
                        AND c.relkind IN ('r','p','v','m','f') AND
                            (has_table_privilege(c.oid, 'INSERT,UPDATE,DELETE,TRUNCATE,TRIGGER')
                                OR has_any_column_privilege(c.oid, 'INSERT,UPDATE')
                                OR (n.nspname='app' AND has_any_column_privilege(c.oid, 'SELECT')))
                    )
                FROM pg_roles r WHERE r.rolname=session_user
                """, connection);
            if (await check.ExecuteScalarAsync(ct) is not true)
                throw new DataFailure("ReportingRoleMismatch", 503);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private static NpgsqlDataSource Create(IConfiguration config)
    {
        if (!int.TryParse(config["Database:Port"], out var port) || port is < 1 or > 65535)
            throw new DataFailure("DatabaseConfiguration", 503, "Database:Port");

        var settings = new NpgsqlConnectionStringBuilder
        {
            Host=Required("Database:Host"), Port=port, Database=Required("Database:Name"),
            Username=Required("Database:Reporting:Username"), Password=Required("Database:Reporting:Password"),
            Timeout=5, CommandTimeout=8
        };
        return NpgsqlDataSource.Create(settings.ConnectionString);

        string Required(string field) => config[field] is { } value && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new DataFailure("DatabaseConfiguration", 503, field);
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
            await _source.Value.DisposeAsync();
    }
}
