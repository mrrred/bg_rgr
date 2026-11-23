using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace BD_RGR.Api.Reports;

internal sealed class ReportResults : IDisposable
{
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = 16 * 1024 * 1024 });

    public void Store(ResultOwner owner, ReportResult result)
    {
        var size = result.Rows.Sum(row => row.Sum(cell => (cell?.Length ?? 0) * 2 + 32L)) + 2048;
        _cache.Set(result.Id, new Snapshot(owner, result), new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(20),
            Size = size
        });
    }

    public ReportResult? Find(ResultOwner owner, Guid id) =>
        _cache.TryGetValue<Snapshot>(id, out var stored) && stored?.Owner == owner ? stored.Result : null;

    public void Dispose() => _cache.Dispose();

    private sealed record Snapshot(ResultOwner Owner, ReportResult Result);
}

internal sealed record ResultOwner(string Session, string Role);

internal sealed class CsvReport
{
    private readonly Dictionary<string, Dictionary<string, string>> _translations = new()
    {
        ["ru"] = Load("ru"),
        ["en"] = Load("en")
    };

    public byte[] Encode(ReportResult result, string language)
    {
        var culture = CultureInfo.GetCultureInfo(language == "ru" ? "ru-RU" : "en-US");
        var texts = _translations[language];
        var output = new StringBuilder();
        var separator = culture.TextInfo.ListSeparator;
        output.AppendJoin(separator, result.Columns.Select(column =>
            Escape(column.TitleKey is { } key ? texts.GetValueOrDefault(key, column.Name) : column.Name, true)));
        output.Append("\r\n");
        foreach (var row in result.Rows)
        {
            for (var i = 0; i < row.Length; i++)
            {
                if (i > 0)
                    output.Append(separator);
                var kind = result.Columns[i].Kind;
                var value = row[i] ?? "";
                if (kind == "date" && DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var date))
                    value = date.ToString("d", culture);
                if (kind == "number" && decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                    value = number.ToString(culture);
                if (kind == "boolean" && value.Length > 0)
                    value = texts[value == "true" ? "Yes" : "No"];
                if (result.Columns[i].Choices?.FirstOrDefault(choice => choice.Value == value) is { } translated)
                    value = texts.GetValueOrDefault(translated.TitleKey, value);
                output.Append(Escape(value, kind != "number"));
            }
            output.Append("\r\n");
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(output.ToString())];
    }

    private static string Escape(string value, bool protectFormula)
    {
        var prefix = value.AsSpan().TrimStart();
        if (protectFormula && (value.StartsWith('\t') || value.StartsWith('\r') || value.StartsWith('\n')
            || prefix.Length > 0 && prefix[0] is '=' or '+' or '-' or '@'))
            value = "'" + value;
        return "\"" + value.Replace("\"", "\"\"") + "\"";
    }

    private static Dictionary<string, string> Load(string language) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Localization", language + ".json")))!;
}
