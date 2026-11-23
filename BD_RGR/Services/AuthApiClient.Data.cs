using System.Net.Http;
using BD_RGR.Models;

namespace BD_RGR.Services;

public sealed partial class AuthApiClient
{
    public Task<BusinessResource[]> ResourcesAsync(Session session) => GetAsync<BusinessResource[]>(session, "tables");

    public Task<BusinessPage> TableAsync(Session session, string resource, string search, string sort, bool descending,
        int page, string? relatedField, int? relatedId)
    {
        var url = $"tables/{resource}?q={Uri.EscapeDataString(search)}&sort={Uri.EscapeDataString(sort)}&desc={descending}&page={page}";
        if (relatedField is not null && relatedId is { } id)
            url += $"&relatedField={Uri.EscapeDataString(relatedField)}&relatedId={id}";
        return GetAsync<BusinessPage>(session, url);
    }

    public Task<BusinessRow> RowAsync(Session session, string resource, int id) =>
        GetAsync<BusinessRow>(session, $"tables/{resource}/{id}");

    public Task<LookupRow[]> LookupAsync(Session session, string resource, string field, string search, string? selected,
        CancellationToken cancellationToken) =>
        GetAsync<LookupRow[]>(session, $"lookups/{resource}/{field}?q={Uri.EscapeDataString(search)}"
            + (selected is null ? "" : $"&selected={Uri.EscapeDataString(selected)}"), cancellationToken);

    public Task<VehicleLink> VehicleLinkAsync(Session session, int id) =>
        GetAsync<VehicleLink>(session, $"vehicle-links/{id}");

    public Task<MileageResult> CalculateMileageAsync(Session session, int id, DateTime from, DateTime to) =>
        GetAsync<MileageResult>(session, FormattableString.Invariant($"mileage?vehicleId={id}&from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}"));

    public async Task<int> SaveRowAsync(Session session, string resource, int? id, Dictionary<string, string?> values)
    {
        var path = id is { } rowId ? $"tables/{resource}/{rowId}" : $"tables/{resource}";
        using var response = await SendAsync(id is null ? HttpMethod.Post : HttpMethod.Put, path, values, session.Token);
        return (await ReadAsync<SavedRow>(response)).Id;
    }

    public async Task DeleteRowAsync(Session session, string resource, int id)
    {
        using var response = await SendAsync(HttpMethod.Delete, $"tables/{resource}/{id}", token: session.Token);
    }

    private async Task<T> GetAsync<T>(Session session, string path, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, path, token: session.Token, cancellationToken: cancellationToken);
        return await ReadAsync<T>(response);
    }

    private sealed record SavedRow(int Id);
}
