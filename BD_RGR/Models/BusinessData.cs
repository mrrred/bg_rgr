namespace BD_RGR.Models;

public sealed record BusinessResource(string Name, string TitleKey, bool IsVehicleSubtype, int PassportFieldCount, string Rights,
    BusinessField[] Fields);

public sealed record BusinessField(string Name, string TitleKey, string Kind, bool Nullable, int MaxLength,
    string? Target, string? Default, BusinessChoice[]? Choices);

public sealed record BusinessChoice(string Value, string TitleKey);
public sealed record BusinessRow(int Id, Dictionary<string, string?> Values, Dictionary<string, string?> Labels);
public sealed record BusinessPage(bool HasMore, BusinessRow[] Rows);
public sealed record LookupRow(int Id, string Label);
public sealed record VehicleLink(string Resource, int Id);
