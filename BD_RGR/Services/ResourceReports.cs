using BD_RGR.Models;

namespace BD_RGR.Services;

internal sealed class ResourceReports(AuthApiClient api, Session session)
{
    private const int MaxRows = 500;
    private readonly ResourceReader _reader = new(api, session);

    private static readonly Dictionary<string, string> Headers = new()
    {
        ["id"] = "FieldId",
        ["start_point"] = "FieldStartPoint",
        ["end_point"] = "FieldEndPoint",
        ["model"] = "Model",
        ["count"] = "FieldQuantity"
    };

    private static readonly Dictionary<string, string> EnumFields = new()
    {
        ["document_type"] = "document_type",
        ["gender_type"] = "gender",
        ["hr_event_type"] = "hr_event_type",
        ["operation_type"] = "operation_type",
        ["vehicle_color"] = "color",
        ["vehicle_status"] = "status"
    };

    internal Task<ReportResult> RunAsync(string template, string sql) => template switch
    {
        "routes" => Routes(),
        "cargo" => Cargo(),
        "mileage" => Mileage(),
        "manual" => Select(sql),
        _ => throw new AuthApiException("RowNotFound")
    };

    private async Task<ReportResult> Routes()
    {
        var rows = await _reader.PageAsync("route", "number,name,start_point_id,end_point_id", MaxRows, CancellationToken.None,
            sort: "number");
        var ids = rows.Rows.SelectMany(row => new[] { row.Int("start_point_id"), row.Int("end_point_id") });
        var points = await Names("point", ids);
        return new(
            [Column("number", "FieldNumber"), Column("name", "FieldName"), Column("start_point", "FieldStartPoint"), Column("end_point", "FieldEndPoint")],
            rows.Rows.Select(row => new string?[]{row.Text("number"),row.Text("name"),
                points.GetValueOrDefault(row.Int("start_point_id")),points.GetValueOrDefault(row.Int("end_point_id"))}).ToArray(), rows.HasMore);
    }

    private async Task<ReportResult> Cargo()
    {
        var rows = await _reader.PageAsync("cargo_transportation", "date,truck_id", MaxRows, CancellationToken.None, sort: "date");
        var trucks = await _reader.MatchingAsync("truck", "registration_number,model_id", "id", rows.Rows.Select(row => row.Int("truck_id")), CancellationToken.None);
        var truckById = trucks.ToDictionary(row => row.Id);
        var models = await Names("model", trucks.Select(row => row.Int("model_id")));
        return new(
            [Column("date", "FieldDate", "date"), Column("registration_number", "RegistrationNumber"), Column("model", "Model")],
            rows.Rows.Where(row => truckById.ContainsKey(row.Int("truck_id"))).Select(row =>
            {
                var truck = truckById[row.Int("truck_id")];
                return new string?[] { row.Text("date"), truck.Text("registration_number"), models.GetValueOrDefault(truck.Int("model_id")) };
            }).ToArray(), rows.HasMore);
    }

    private async Task<ReportResult> Mileage()
    {
        var rows = await _reader.PageAsync("vehicle_mileage_log", "reading_date,vehicle_id,mileage", MaxRows, CancellationToken.None,
            sort: "reading_date");
        var vehicles = await _reader.MatchingAsync("vehicle", "registration_number", "id", rows.Rows.Select(row => row.Int("vehicle_id")), CancellationToken.None);
        var plates = vehicles.ToDictionary(row => row.Id, row => row.Text("registration_number"));
        return new([Column("reading_date", "FieldDate", "date"), Column("registration_number", "RegistrationNumber"), Column("mileage", "Mileage", "number")],
            rows.Rows.Select(row => new string?[] { row.Text("reading_date"), plates.GetValueOrDefault(row.Int("vehicle_id")), row.Text("mileage") }).ToArray(), rows.HasMore);
    }

    private async Task<ReportResult> Select(string sql)
    {
        var result = await api.SelectAsync(session, sql);
        var catalog = await api.ResourcesAsync(session);
        var fields = catalog.SelectMany(resource => resource.Fields).GroupBy(field => field.Name)
            .ToDictionary(group => group.Key, group => group.First());
        var columns = result.Columns.Select(column =>
        {
            var label = Headers.GetValueOrDefault(column.Name) ?? fields.GetValueOrDefault(column.Name)?.TitleKey;
            var choices = EnumFields.TryGetValue(column.DatabaseType, out var field) ? fields.GetValueOrDefault(field)?.Choices : null;
            return new ReportColumn(column.Name, label, column.Kind, choices);
        }).ToArray();
        return new(columns, result.Rows, result.Truncated);
    }

    private async Task<Dictionary<int, string>> Names(string resource, IEnumerable<int> ids) =>
        (await _reader.MatchingAsync(resource, "name", "id", ids, CancellationToken.None)).ToDictionary(row => row.Id, row => row.Text("name"));

    private static ReportColumn Column(string name, string title, string kind = "text") => new(name, title, kind, null);
}
