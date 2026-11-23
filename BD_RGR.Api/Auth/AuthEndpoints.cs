using System.Net.Http.Headers;
using Microsoft.AspNetCore.Identity;
using Npgsql;

namespace BD_RGR.Api.Auth;

internal sealed class AuthEndpoints(
    AccountsDatabase accounts, SessionStore sessions, IPasswordHasher<string> hasher, ILogger<AuthEndpoints> logger)
{
    public async Task<IResult> RolesAsync(CancellationToken cancellationToken)
    {
        var roles = await accounts.GetRolesAsync(cancellationToken);
        return roles.Count == AccountsDatabase.RoleCodes.Length
            ? Results.Ok(roles)
            : Error("RoleCatalogUnavailable", StatusCodes.Status503ServiceUnavailable);
    }

    public async Task<IResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        var password = request.Password;
        if (!ValidUsername(username) || !ValidPassword(password))
            return Error("RequiredCredentials");
        if (password != request.RepeatedPassword)
            return Error("PasswordMismatch");
        if (request.RoleId is not > 0)
            return Error("RequiredRole");

        var hash = hasher.HashPassword(username!, password!);
        try
        {
            return await accounts.InsertAsync(username!, hash, request.RoleId.Value, cancellationToken) switch
            {
                RegistrationOutcome.Created => Results.StatusCode(StatusCodes.Status201Created),
                RegistrationOutcome.DuplicateUsername => Error("DuplicateUsername", StatusCodes.Status409Conflict),
                _ => Error("RequiredRole")
            };
        }
        catch (PostgresException e) when (e.SqlState == PostgresErrorCodes.ForeignKeyViolation
            && e.ConstraintName == "users_role_fk")
        {
            return Error("RequiredRole");
        }
    }

    public async Task<IResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username?.Trim();
        if (!ValidUsername(username) || !ValidPassword(request.Password))
            return Error("InvalidCredentials", StatusCodes.Status401Unauthorized);

        var account = await accounts.FindAsync(username!, cancellationToken);
        if (account is null || !MatchesPassword(account, request.Password!))
            return Error("InvalidCredentials", StatusCodes.Status401Unauthorized);
        if (account.RoleCode is not { } roleCode || !AccountsDatabase.RoleCodes.Contains(roleCode))
            return Error("RoleUnavailable", StatusCodes.Status403Forbidden);

        return Results.Ok(new LoginResponse(sessions.Create(account), account.Username, roleCode));
    }

    public IResult Logout(HttpContext context)
    {
        if (BearerToken(context) is not { } token)
            return Error("SessionExpired", StatusCodes.Status401Unauthorized);

        sessions.Revoke(token);
        return Results.NoContent();
    }

    public async Task<IResult> PasswordAsync(HttpContext context, PasswordRequest request, CancellationToken cancellationToken)
    {
        if (BearerToken(context) is not { } token || sessions.Find(token) is not { } session)
            return Error("SessionExpired", StatusCodes.Status401Unauthorized);

        var account = await accounts.FindAsync(session.UserId, cancellationToken);
        if (account is null || SessionStore.Fingerprint(account.PasswordHash) != session.PasswordFingerprint)
        {
            sessions.Revoke(token);
            return Error("SessionExpired", StatusCodes.Status401Unauthorized);
        }

        if (account.RoleCode is not { } code || !AccountsDatabase.RoleCodes.Contains(code))
            return Error("RoleUnavailable", StatusCodes.Status403Forbidden);

        if (!ValidPassword(request.CurrentPassword) || !MatchesPassword(account, request.CurrentPassword!))
            return Error("InvalidCurrentPassword");
        if (!ValidPassword(request.NewPassword))
            return Error("RequiredNewPassword");
        if (request.NewPassword != request.RepeatedPassword)
            return Error("PasswordMismatch");

        var hash = hasher.HashPassword(account.Username, request.NewPassword!);
        if (!await accounts.UpdateHashAsync(account.Id, account.PasswordHash, hash, cancellationToken))
            return Error("SessionExpired", StatusCodes.Status401Unauthorized);

        // A login racing with this update may have read the old hash. PasswordAsync also checks its fingerprint.
        sessions.RevokePasswordSessions(session);
        return Results.NoContent();
    }

    public async Task<IResult> WithDatabaseAsync(Func<Task<IResult>> request)
    {
        try
        {
            return await request();
        }
        catch (AccountsConfigurationException e)
        {
            return Error("DatabaseConfiguration", StatusCodes.Status503ServiceUnavailable, e.Field);
        }
        catch (PostgresException e)
        {
            logger.LogWarning("Accounts database SQLSTATE {SqlState}.", e.SqlState);
            var code = e.SqlState switch
            {
                PostgresErrorCodes.InsufficientPrivilege => "DatabasePermissions",
                PostgresErrorCodes.InvalidColumnReference => "DatabaseSchema",
                _ => "DatabaseUnavailable"
            };
            return Error(code, StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception e) when (e is NpgsqlException or TimeoutException)
        {
            logger.LogWarning("Accounts database connection or command failed.");
            return Error("DatabaseUnavailable", StatusCodes.Status503ServiceUnavailable);
        }
    }

    private bool MatchesPassword(Account account, string password)
    {
        try
        {
            return hasher.VerifyHashedPassword(account.Username, account.PasswordHash, password)
                != PasswordVerificationResult.Failed;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool ValidUsername(string? username) => username is { Length: > 0 and <= 128 }
        && !username.Contains('\0');

    private static bool ValidPassword(string? password) => password is { Length: <= 1024 }
        && !string.IsNullOrWhiteSpace(password);

    internal static string? BearerToken(HttpContext context) =>
        AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out var header)
        && string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
        && header.Parameter is { Length: 64 } token
            ? token
            : null;

    private static IResult Error(string code, int status = StatusCodes.Status400BadRequest, string? field = null) =>
        Results.Json(new { code, field }, statusCode: status);
}

internal static class AuthRouting
{
    public static void MapAuthEndpoints(this WebApplication app)
    {
        var auth = app.MapGroup("/auth");
        auth.AddEndpointFilter(async (context, next) =>
        {
            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });
        auth.MapGet("/roles", (AuthEndpoints api, CancellationToken ct) =>
            api.WithDatabaseAsync(() => api.RolesAsync(ct)));
        auth.MapPost("/register", (AuthEndpoints api, RegisterRequest request, CancellationToken ct) =>
            api.WithDatabaseAsync(() => api.RegisterAsync(request, ct)));
        auth.MapPost("/login", (AuthEndpoints api, LoginRequest request, CancellationToken ct) =>
            api.WithDatabaseAsync(() => api.LoginAsync(request, ct)));
        auth.MapPost("/logout", (AuthEndpoints api, HttpContext context) => api.Logout(context));
        auth.MapPost("/password", (AuthEndpoints api, HttpContext context, PasswordRequest request, CancellationToken ct) =>
            api.WithDatabaseAsync(() => api.PasswordAsync(context, request, ct)));
    }
}

internal sealed record RegisterRequest(string? Username, string? Password, string? RepeatedPassword, int? RoleId);
internal sealed record LoginRequest(string? Username, string? Password);
internal sealed record LoginResponse(string Token, string Username, string RoleCode);
internal sealed record PasswordRequest(string? CurrentPassword, string? NewPassword, string? RepeatedPassword);
