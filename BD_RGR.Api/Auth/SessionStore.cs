using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace BD_RGR.Api.Auth;

internal sealed class SessionStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromHours(8);
    private readonly ConcurrentDictionary<string, ServerSession> _sessions = new();

    public string Create(Account account)
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, session) in _sessions)
            if (session.ExpiresAt <= now)
                _sessions.TryRemove(key, out _);

        var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _sessions[Fingerprint(token)] = new ServerSession(account.Id, Fingerprint(account.PasswordHash), now + Lifetime);
        return token;
    }

    public ServerSession? Find(string token)
    {
        var key = Fingerprint(token);
        if (!_sessions.TryGetValue(key, out var session))
            return null;

        if (session.ExpiresAt > DateTimeOffset.UtcNow)
            return session;

        _sessions.TryRemove(key, out _);
        return null;
    }

    public void Revoke(string token) => _sessions.TryRemove(Fingerprint(token), out _);

    public async Task<string?> ResolveRoleAsync(HttpContext context, AccountsDatabase accounts, CancellationToken ct)
    {
        if (AuthEndpoints.BearerToken(context) is not { } token || Find(token) is not { } session)
            return null;

        var account = await accounts.FindAsync(session.UserId, ct);
        if (account?.RoleCode is { } role && AccountsDatabase.RoleCodes.Contains(role)
            && Fingerprint(account.PasswordHash) == session.PasswordFingerprint)
            return role;

        Revoke(token);
        return null;
    }

    public void RevokePasswordSessions(ServerSession previous)
    {
        foreach (var (key, session) in _sessions)
            if (session.UserId == previous.UserId && session.PasswordFingerprint == previous.PasswordFingerprint)
                _sessions.TryRemove(key, out _);
    }

    public static string Fingerprint(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}

internal sealed record ServerSession(int UserId, string PasswordFingerprint, DateTimeOffset ExpiresAt);
