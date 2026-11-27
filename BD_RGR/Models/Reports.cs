namespace BD_RGR.Models;

public sealed record ReportColumn(string Name, string? TitleKey, string Kind, BusinessChoice[]? Choices);
public sealed record ReportResult(ReportColumn[] Columns, string?[][] Rows, bool Truncated);

internal sealed record QueryColumn(string Name, string Kind, string DatabaseType);
internal sealed record QueryResult(QueryColumn[] Columns, string?[][] Rows, bool Truncated);
