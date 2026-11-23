namespace BD_RGR.Models;

public sealed record ReportColumn(string Name, string? TitleKey, string Kind, BusinessChoice[]? Choices);
public sealed record ReportResult(Guid Id, ReportColumn[] Columns, string?[][] Rows, bool Truncated);
public sealed record AnalyticsMetric(string Name, string TitleKey);
public sealed record AnalyticsPoint(DateOnly Date, long Value);
public sealed record AnalyticsResult(DateOnly From, DateOnly To, AnalyticsPoint[] Points, DateTimeOffset UpdatedAt);
public sealed record ChartSnapshot(AnalyticsMetric Metric, AnalyticsResult Result);
