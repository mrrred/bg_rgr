using System.Globalization;
using BD_RGR.Models;

namespace BD_RGR.Services;

internal sealed class ResourceReader(AuthApiClient api, Session session)
{
    public async Task<BusinessRow[]> AllAsync(string resource, string fields, CancellationToken ct, params string[] filters) =>
        (await api.ReadAsync(session, resource, fields, filters, null, null, false, null, ct)).Rows;

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

    public Task<BusinessPage> PageAsync(string resource, string fields, int limit, CancellationToken ct,
        string[]? filters = null, string? sort = null, bool descending = false) =>
        api.ReadAsync(session, resource, fields, filters ?? [], limit, sort, descending, null, ct);

    public async Task<BusinessRow[]> RepresentativesAsync(string resource, string fields, string sort, string group,
        bool descending, CancellationToken ct, params string[] filters) =>
        (await api.ReadAsync(session, resource, fields, filters, null, sort, descending, group, ct)).Rows;

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
