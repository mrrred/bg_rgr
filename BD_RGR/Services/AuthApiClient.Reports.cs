using System.Net.Http;
using BD_RGR.Models;

namespace BD_RGR.Services;

public sealed partial class AuthApiClient
{
    internal async Task<QueryResult> SelectAsync(Session session, string sql, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "queries/select", new { sql }, session.Token, cancellationToken);
        return await ReadAsync<QueryResult>(response);
    }
}
