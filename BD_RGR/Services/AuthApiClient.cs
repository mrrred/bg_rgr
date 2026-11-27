using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using BD_RGR.Models;

namespace BD_RGR.Services;

public sealed partial class AuthApiClient : IDisposable
{
    private readonly HttpClient _http;

    public AuthApiClient(Uri baseAddress)
    {
        _http = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false, UseProxy = false })
        {
            BaseAddress = baseAddress,
            Timeout = TimeSpan.FromSeconds(15)
        };
    }

    public async Task<IReadOnlyList<RegistrationRole>> GetRolesAsync()
    {
        using var response = await SendAsync(HttpMethod.Get, "auth/roles");
        var rows = await ReadAsync<RoleResponse?[]>(response);
        var roles = new List<RegistrationRole>(rows.Length);
        foreach (var row in rows)
        {
            if (row is null || row.Id <= 0)
                throw new AuthApiException("ApiUnexpectedResponse");

            var role = Role.All.SingleOrDefault(item => item.Code == row.Code);
            if (role is null)
                throw new AuthApiException("ApiUnexpectedResponse");

            roles.Add(new RegistrationRole(row.Id, role));
        }

        if (roles.Select(option => option.Role).Distinct().Count() != Role.All.Count
            || roles.Select(option => option.Id).Distinct().Count() != Role.All.Count
            || roles.Count != Role.All.Count)
        {
            throw new AuthApiException("ApiUnexpectedResponse");
        }

        return roles;
    }

    public async Task RegisterAsync(string username, string password, string repeatedPassword, int roleId)
    {
        using var response = await SendAsync(HttpMethod.Post, "auth/register",
            new { username, password, repeatedPassword, roleId });
    }

    public async Task<Session> SignInAsync(string username, string password)
    {
        using var response = await SendAsync(HttpMethod.Post, "auth/login", new { username, password });
        var login = await ReadAsync<LoginResponse>(response);
        var role = Role.All.SingleOrDefault(item => item.Code == login.RoleCode);
        if (role is null || string.IsNullOrWhiteSpace(login.Username) || login.Token is not { Length: 64 })
            throw new AuthApiException("ApiUnexpectedResponse");

        return new Session(login.Username, role, login.Token);
    }

    public async Task SignOutAsync(Session session, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "auth/logout", token: session.Token,
            cancellationToken: cancellationToken);
    }

    public async Task ChangePasswordAsync(Session session, string currentPassword, string newPassword, string repeatedPassword)
    {
        using var response = await SendAsync(HttpMethod.Post, "auth/password",
            new { currentPassword, newPassword, repeatedPassword }, session.Token);
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null,
        string? token = null, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body);
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _http.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
                return response;

            using (response)
            {
                var error = await ReadAsync<ErrorResponse>(response);
                throw error.Code switch
                {
                    "DatabaseConfiguration" when error.Field?.StartsWith("Database:", StringComparison.Ordinal) == true
                        => new AuthApiException(error.Code, error.Field),
                    "RequiredCredentials" or "PasswordMismatch" or "RequiredRole" or "DuplicateUsername"
                        or "InvalidCredentials" or "RoleUnavailable" or "SessionExpired"
                        or "InvalidCurrentPassword" or "RequiredNewPassword" or "DatabasePermissions"
                        or "DatabaseUnavailable" or "DatabaseSchema" or "RoleCatalogUnavailable" => new AuthApiException(error.Code),
                    "BusinessDatabaseUnavailable" or "BusinessSchemaMismatch" or "BusinessRoleMismatch"
                        or "RequiredField" or "InvalidField" or "InvalidReference"
                        or "RowNotFound" or "DuplicateRow" or "ValidationFailed" or "HasDependencies"
                        or "OperationDenied" or "AmbiguousVehicle" or "InvalidPeriod" or "ReportInvalidSql"
                        or "ReportSourceDenied" or "ReportFunctionDenied" or "ReportTooManyColumns" or "ReportingRoleMismatch"
                        or "ReportTimeout" or "AnalyticsPeriodInvalid" => new AuthApiException(error.Code, error.Field),
                    _ => new AuthApiException("ApiUnexpectedResponse")
                };
            }
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        {
            throw new AuthApiException("ApiUnavailable");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<T>()
                ?? throw new AuthApiException("ApiUnexpectedResponse");
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or IOException)
        {
            throw new AuthApiException("ApiUnexpectedResponse");
        }
    }

    public void Dispose() => _http.Dispose();

    private sealed record RoleResponse(int Id, string? Code);
    private sealed record LoginResponse(string? Token, string? Username, string? RoleCode);
    private sealed record ErrorResponse(string? Code, string? Field);
}

public sealed class AuthApiException(string key, string? field = null) : Exception
{
    public AuthNotice Notice { get; } = new(key, field);
}

public sealed record AuthNotice(string Key, string? Field = null);
