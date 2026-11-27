using System.Net.Http;
using System.Runtime.CompilerServices;
using BD_RGR.Models;

namespace BD_RGR.Services;

public sealed partial class AuthApiClient
{
    private readonly ConditionalWeakTable<Session, ResourceCatalogRequest> _resourceCatalogs = new();

    public Task<BusinessResource[]> ResourcesAsync(Session session, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return _resourceCatalogs.GetValue(session, static _ => new ResourceCatalogRequest())
            .GetAsync(() => GetAsync<BusinessResource[]>(session, "tables"), cancellationToken);
    }

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

    internal Task<BusinessPage> ReadPageAsync(Session session, string resource, string fields, string[] filters,
        int size, int page, int? afterId, string? sort, bool descending, string? latestBy, CancellationToken cancellationToken)
    {
        var url = $"tables/{resource}?labels=false&fields={Uri.EscapeDataString(fields)}&pageSize={size}&page={page}";
        if (afterId is { } cursor)
            url += $"&afterId={cursor}";
        if (sort is not null)
            url += $"&sort={Uri.EscapeDataString(sort)}&desc={descending}";
        if (latestBy is not null)
            url += $"&latestBy={Uri.EscapeDataString(latestBy)}";
        foreach (var filter in filters)
            url += $"&filter={Uri.EscapeDataString(filter)}";
        return GetAsync<BusinessPage>(session, url, cancellationToken);
    }

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

    private sealed class ResourceCatalogRequest
    {
        private readonly object _gate = new();
        private Task<BusinessResource[]>? _request;

        public Task<BusinessResource[]> GetAsync(Func<Task<BusinessResource[]>> load, CancellationToken cancellationToken)
        {
            lock (_gate)
            {
                if (_request is null || _request.IsFaulted || _request.IsCanceled)
                    _request = load();

                // Closing one screen cancels its wait while other screens can still use the request.
                return _request.WaitAsync(cancellationToken);
            }
        }
    }

    private sealed record SavedRow(int Id);
}
