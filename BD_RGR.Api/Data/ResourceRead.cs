using System.Globalization;
using Npgsql;
using NpgsqlTypes;

namespace BD_RGR.Api.Data;

internal sealed record ResourceFilter(Field Field, string Operator, string[] Values);

internal sealed record ResourceRead(int Page, int PageSize, Field[] Fields, bool Labels, Field? Sort,
    bool Descending, int? AfterId, Field? LatestBy, ResourceFilter[] Filters)
{
    private static readonly Field Identifier = new("id", "FieldId", "integer");
    private static readonly Dictionary<string, string> Comparisons = new()
    {
        ["eq"] = "=",
        ["ne"] = "<>",
        ["gt"] = ">",
        ["gte"] = ">=",
        ["lt"] = "<",
        ["lte"] = "<="
    };

    public static ResourceRead Parse(Resource resource, int? page, int? size, string? columns, bool? labels,
        string? sort, bool? descending, int? afterId, string? latestBy, string[] filters)
    {
        if (page is < 0 or > 1_000_000 || size is < 1 or > 500 || filters.Length > 20 ||
            afterId is < 0 || (afterId is not null && (page is > 0 || sort is not null and not "id" || descending == true || latestBy is not null)))
            throw new DataFailure("InvalidField");

        var fields = resource.ReadFields.Prepend(Identifier).ToDictionary(field => field.Name, StringComparer.Ordinal);
        Field Find(string name) => fields.TryGetValue(name, out var field)
            ? field : throw new DataFailure("InvalidField", field: name);

        var projection = columns is null ? resource.DisplayFields.ToArray() :
            columns.Split(',').Distinct(StringComparer.Ordinal).Select(Find).Where(field => field.Name != "id").ToArray();
        var filterList = filters.Select(value => ParseFilter(Find, value)).ToArray();
        var sortField = sort is null or "id" ? null : Find(sort);
        var partition = latestBy is null ? null : Find(latestBy);
        if (partition is not null && (partition.Name == "id" || sortField is null))
            throw new DataFailure("InvalidField");

        return new(page ?? 0, size ?? TableQueries.PageSize, projection, labels ?? true, sortField, descending ?? false,
            afterId, partition, filterList);
    }

    public static string Condition(NpgsqlCommand command, Resource resource, ResourceFilter filter, int index)
    {
        var column = TableQueries.Column(resource, filter.Field);
        var name = $"filter{index}";
        if (filter.Operator == "null")
            return $"{column} IS NULL";
        if (filter.Operator == "notnull")
            return $"{column} IS NOT NULL";
        if (filter.Operator == "in")
        {
            command.Parameters.AddWithValue(name, NpgsqlDbType.Array | NpgsqlDbType.Text, filter.Values);
            return $"{column} = ANY(@{name}::{filter.Field.Cast}[])";
        }

        command.Parameters.AddWithValue(name, NpgsqlDbType.Text, filter.Values[0]);
        return filter.Operator == "contains"
            ? $"strpos(lower({column}::text COLLATE pg_catalog.\"unicode\"), lower(@{name} COLLATE pg_catalog.\"unicode\")) > 0"
            : $"{column} {Comparisons[filter.Operator]} @{name}::{filter.Field.Cast}";
    }

    private static ResourceFilter ParseFilter(Func<string, Field> find, string input)
    {
        if (input.Length > 4096)
            throw new DataFailure("InvalidField");
        var parts = input.Split(':', 3);
        if (parts.Length < 2)
            throw new DataFailure("InvalidField");
        var field = find(parts[0]);
        var operation = parts[1];
        if (operation is "null" or "notnull" && parts.Length == 2)
            return new(field, operation, []);
        if (parts.Length != 3 || (!Comparisons.ContainsKey(operation) && operation is not ("in" or "contains")))
            throw new DataFailure("InvalidField", field: field.Name);
        if (operation == "contains" && field.Kind != "text")
            throw new DataFailure("InvalidField", field: field.Name);
        var values = operation == "in" ? parts[2].Split(',') : [parts[2]];
        if (values.Length > 200 || (operation == "in" && field.Kind is not ("integer" or "reference")))
            throw new DataFailure("InvalidField", field: field.Name);
        for (var i = 0; i < values.Length; i++)
        {
            var normalized = TableWrites.Validate(field, values[i]);
            if (normalized is null || (field.Name == "id" && int.Parse(normalized, CultureInfo.InvariantCulture) <= 0))
                throw new DataFailure("InvalidField", field: field.Name);
            values[i] = normalized;
        }
        return new(field, operation, values);
    }
}
