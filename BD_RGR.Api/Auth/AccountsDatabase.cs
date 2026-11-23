using Npgsql;

namespace BD_RGR.Api.Auth;

internal sealed class AccountsDatabase(IConfiguration configuration) : IAsyncDisposable
{
    internal static readonly string[] RoleCodes =
        ["director", "pass_dispatcher", "cargo_dispatcher", "garage_engineer", "reader"];

    private readonly Lazy<NpgsqlDataSource> _source = new(() => CreateDataSource(configuration));

    public async Task<IReadOnlyList<AccountRole>> GetRolesAsync(CancellationToken cancellationToken)
    {
        await using var command = _source.Value.CreateCommand("""
            SELECT id, code
            FROM app.roles
            WHERE code = ANY($1)
            ORDER BY id
            """);
        command.Parameters.AddWithValue(RoleCodes);

        var roles = new List<AccountRole>();
        await using var rows = await command.ExecuteReaderAsync(cancellationToken);
        while (await rows.ReadAsync(cancellationToken))
            roles.Add(new AccountRole(rows.GetInt32(0), rows.GetString(1)));

        return roles;
    }

    public async Task<RegistrationOutcome> InsertAsync(string username, string hash, int roleId,
        CancellationToken cancellationToken)
    {
        await using var command = _source.Value.CreateCommand("""
            WITH selected_role AS (
                SELECT id
                FROM app.roles
                WHERE id = $3 AND code = ANY($4)
            ), inserted_user AS (
                INSERT INTO app.users (username, password_hash, id_role)
                SELECT $1, $2, id FROM selected_role
                ON CONFLICT (lower(username)) DO NOTHING
                RETURNING id
            )
            SELECT CASE
                WHEN EXISTS (SELECT 1 FROM inserted_user) THEN 'created'
                WHEN EXISTS (SELECT 1 FROM selected_role) THEN 'duplicate'
                ELSE 'missing_role'
            END
            """);
        command.Parameters.AddWithValue(username);
        command.Parameters.AddWithValue(hash);
        command.Parameters.AddWithValue(roleId);
        command.Parameters.AddWithValue(RoleCodes);
        return await command.ExecuteScalarAsync(cancellationToken) switch
        {
            "created" => RegistrationOutcome.Created,
            "duplicate" => RegistrationOutcome.DuplicateUsername,
            _ => RegistrationOutcome.MissingRole
        };
    }

    public async Task<Account?> FindAsync(string username, CancellationToken cancellationToken)
    {
        await using var command = _source.Value.CreateCommand("""
            SELECT u.id, u.username, u.password_hash, r.code
            FROM app.users AS u
            LEFT JOIN app.roles AS r ON r.id = u.id_role
            WHERE lower(u.username) = lower($1)
            """);
        command.Parameters.AddWithValue(username);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new Account(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3))
            : null;
    }

    public async Task<Account?> FindAsync(int userId, CancellationToken cancellationToken)
    {
        await using var command = _source.Value.CreateCommand("""
            SELECT u.id, u.username, u.password_hash, r.code
            FROM app.users AS u
            LEFT JOIN app.roles AS r ON r.id = u.id_role
            WHERE u.id = $1
            """);
        command.Parameters.AddWithValue(userId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken)
            ? new Account(reader.GetInt32(0), reader.GetString(1), reader.GetString(2),
                reader.IsDBNull(3) ? null : reader.GetString(3))
            : null;
    }

    public async Task<bool> UpdateHashAsync(int userId, string expectedHash, string newHash, CancellationToken cancellationToken)
    {
        await using var command = _source.Value.CreateCommand("""
            UPDATE app.users
            SET password_hash = $1
            WHERE id = $2 AND password_hash = $3
            """);
        command.Parameters.AddWithValue(newHash);
        command.Parameters.AddWithValue(userId);
        command.Parameters.AddWithValue(expectedHash);
        return await command.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    private static NpgsqlDataSource CreateDataSource(IConfiguration config)
    {
        var host = Required("Database:Host");
        if (!int.TryParse(config["Database:Port"], out var port) || port is < 1 or > 65535)
            throw new AccountsConfigurationException("Database:Port");

        var settings = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = Required("Database:Name"),
            Username = Required("Database:Accounts:Username"),
            Password = Required("Database:Accounts:Password"),
            Timeout = 5,
            CommandTimeout = 8
        };
        return NpgsqlDataSource.Create(settings.ConnectionString);

        string Required(string key) => string.IsNullOrWhiteSpace(config[key])
            ? throw new AccountsConfigurationException(key)
            : config[key]!;
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
            await _source.Value.DisposeAsync();
    }
}

internal sealed class AccountsConfigurationException(string field) : Exception
{
    public string Field { get; } = field;
}

internal sealed record Account(int Id, string Username, string PasswordHash, string? RoleCode);
internal sealed record AccountRole(int Id, string Code);

internal enum RegistrationOutcome
{
    Created,
    DuplicateUsername,
    MissingRole
}
