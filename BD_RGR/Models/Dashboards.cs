namespace BD_RGR.Models;

public sealed record DashboardScreen(string Name, string GroupKey, string TitleKey, string[] Filters, bool HasPeriod);
public sealed record DashboardRequest(DateOnly? From, DateOnly? To, Dictionary<string, string?> Values);
public sealed record DashboardOption(string? Value, string Label, bool Localized, int? GarageId, string? Category);
public sealed record DashboardCard(string TitleKey, decimal? Value, string? UnitKey, string? Target,
    string? Filter, string? Choice, DateOnly? StartReading, DateOnly? EndReading);
public sealed record DashboardLabel(string Text, string? Choice, bool Localized);
public sealed record DashboardSeries(string? TitleKey, decimal?[] Values);
public sealed record DashboardPlot(string TitleKey, string Kind, string UnitKey, DashboardLabel[] Labels,
    DashboardSeries[] Series, string? Filter)
{
    public bool HasValues => Series.Any(series => series.Values.Any(value => value is not null)) &&
        (Kind != "donut" || Series[0].Values.Sum() > 0);
}
public sealed record DashboardTable(string TitleKey, DashboardLabel[] Columns, object?[][] Rows, bool Heat)
{
    public bool IsEmpty => Rows.Length == 0;
}
public sealed record DashboardResult(DateTimeOffset UpdatedAt, DashboardCard[] Cards, DashboardPlot[] Charts,
    DashboardTable[] Tables, string? SelectedVehicle);
