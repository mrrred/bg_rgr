using System.Globalization;
using BD_RGR.Models;

namespace BD_RGR.Services;

internal sealed class ResourceReader(AuthApiClient api, Session session)
{
    public async Task<BusinessRow[]> AllAsync(string resource, string fields, CancellationToken ct, params string[] filters)
    {
        var result = new List<BusinessRow>();
        var cursor = 0;
        while (true)
        {
            var page = await PageAsync(resource, fields, ct, filters, afterId: cursor);
            result.AddRange(page.Rows);
            if (!page.HasMore)
                return result.ToArray();
            cursor = page.Rows[^1].Id;
        }
    }

    public async Task<BusinessRow[]> MatchingAsync(string resource, string fields, string field, IEnumerable<int> ids,
        CancellationToken ct, params string[] filters)
    {
        var result = new List<BusinessRow>();
        foreach (var batch in ids.Distinct().Chunk(150))
        {
            var request = filters.Append($"{field}:in:{string.Join(',', batch.Select(Id))}").ToArray();
            result.AddRange(await AllAsync(resource, fields, ct, request));
        }
        return result.ToArray();
    }

    public Task<BusinessPage> PageAsync(string resource, string fields, CancellationToken ct, string[]? filters = null,
        int size = 250, int page = 0, int? afterId = null, string? sort = null, bool descending = false, string? latestBy = null) =>
        api.ReadPageAsync(session, resource, fields, filters ?? [], size, page, afterId, sort, descending, latestBy, ct);

    public async Task<BusinessRow[]> RepresentativesAsync(string resource, string fields, string sort, string group,
        CancellationToken ct, params string[] filters)
    {
        var rows = new List<BusinessRow>();
        for (var index = 0; ; index++)
        {
            var page = await PageAsync(resource, fields, ct, filters, page: index, sort: sort, descending: true, latestBy: group);
            rows.AddRange(page.Rows);
            if (!page.HasMore)
                return rows.ToArray();
        }
    }

    internal static string BoundsDate(DateOnly value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    internal static string Id(int value) => value.ToString(CultureInfo.InvariantCulture);
}

internal static class ResourceValues
{
    internal static string? Value(this BusinessRow row, string name) => row.Values.GetValueOrDefault(name);
    internal static string Text(this BusinessRow row, string name) => row.Value(name) ?? "";
    internal static int Int(this BusinessRow row, string name) => int.Parse(row.Text(name), CultureInfo.InvariantCulture);
    internal static decimal Decimal(this BusinessRow row, string name) => decimal.Parse(row.Text(name), CultureInfo.InvariantCulture);
    internal static DateOnly Date(this BusinessRow row, string name) =>
        DateOnly.ParseExact(row.Text(name), "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
