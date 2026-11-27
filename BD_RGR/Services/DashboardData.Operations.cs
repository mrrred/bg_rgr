using BD_RGR.Models;

namespace BD_RGR.Services;

internal sealed partial class DashboardData
{
    private sealed record FleetRow(int Id, string Plate, int Garage, string Status, string Category);

    private async Task<FleetRow[]> FleetRows(string[] filters, CancellationToken ct)
    {
        var rows = await _reader.AllAsync("vehicle", "registration_number,garage_id,status", ct, filters);
        var typeLists = await Task.WhenAll(VehicleTypes.Select(async type =>
            (type.Category, Rows: await _reader.MatchingAsync(type.Resource, "vehicle_id", "vehicle_id", rows.Select(row => row.Id), ct))));
        var categories = typeLists.SelectMany(type => type.Rows.Select(row => (Id: row.Int("vehicle_id"), type.Category)))
            .GroupBy(row => row.Id).ToDictionary(group => group.Key, group => group.Count() == 1 ? group.First().Category : "unknown");
        return rows.Select(row => new FleetRow(row.Id, row.Text("registration_number"), row.Int("garage_id"), row.Text("status"),
                categories.GetValueOrDefault(row.Id) ?? "unknown"))
            .OrderBy(row => row.Plate, StringComparer.CurrentCulture).ThenBy(row => row.Id).ToArray();
    }

    private async Task<FleetRow[]> ScopedFleet(Build b, CancellationToken ct)
    {
        var rows = await FleetRows(b.Where(("garage_id", "garage"), ("status", "status")), ct);
        return b.Value("category") is { } category ? rows.Where(row => row.Category == category).ToArray() : rows;
    }

    private async Task Fleet(Build b, CancellationToken ct)
    {
        var rows = await ScopedFleet(b, ct);
        var garages = await _reader.MatchingAsync("garage", "number,name", "id", rows.Select(row => row.Garage), ct);
        var names = garages.ToDictionary(row => row.Id, Named);
        var garageCounts = rows.GroupBy(row => (Garage: OrId(names, row.Garage), row.Status))
            .ToDictionary(group => group.Key, group => group.Count());
        var totals = rows.GroupBy(row => row.Status).ToDictionary(group => group.Key, group => group.Count());

        b.Card("DashboardVehicleCount", rows.Length);
        foreach (var status in Statuses)
            b.Card(status == "неисправен" ? "VehiclesRequiringRepair" : StatusTitle(status), totals.GetValueOrDefault(status),
                Count, "fleet", "status", status);
        b.Chart("DashboardStatusChart", "donut", Count, Statuses.Select(status =>
            (Label(StatusTitle(status), localized: true), (decimal?)totals.GetValueOrDefault(status))));
        b.Matrix("DashboardGarageStack", "stack", garageCounts.Keys.Select(key => key.Garage).Distinct().Order().ToArray(),
            Statuses, StatusTitle, (garage, status) => garageCounts.GetValueOrDefault((garage, status)));
        b.Table("DashboardFleetTable", ["Vehicle", "Garage", "DashboardCategory", "Status"], rows.Select(row =>
            new object?[] { row.Plate, OrId(names,row.Garage), Label(CategoryTitle(row.Category),localized:true),
                Label(StatusTitle(row.Status),localized:true) }));
    }

    private async Task Mileage(Build b, CancellationToken ct)
    {
        var fleet = await ScopedFleet(b, ct);
        var selected = fleet.FirstOrDefault(row => ResourceReader.Id(row.Id) == b.Value("vehicle")) ?? fleet.FirstOrDefault();
        b.Vehicle = selected is null ? null : ResourceReader.Id(selected.Id);
        var first = ReadBounds(fleet, b.From, ct);
        var last = b.From == b.To ? first : ReadBounds(fleet, b.To, ct);
        var readings = selected is null ? Task.FromResult(Array.Empty<BusinessRow>()) :
            _reader.AllAsync("vehicle_mileage_log", "reading_date,mileage", ct, b.Period("reading_date")
                .Append($"vehicle_id:eq:{ResourceReader.Id(selected.Id)}").ToArray());
        await Task.WhenAll(first, last, readings);
        var bounds = fleet.Select(row =>
        {
            var start = first.Result.GetValueOrDefault(row.Id);
            var end = last.Result.GetValueOrDefault(row.Id);
            var a = start?.Date("reading_date");
            var z = end?.Date("reading_date");
            return new MileageItem(row, a, z, a < z ? end!.Decimal("mileage") - start!.Decimal("mileage") : null);
        }).OrderByDescending(row => row.Distance).ThenBy(row => row.Vehicle.Plate).ToArray();
        var current = bounds.FirstOrDefault(row => row.Vehicle == selected);
        b.Cards.Add(new("DashboardDistance", current?.Distance, Km, null, null, null, current?.Start, current?.End));
        b.Card("DashboardReadings", readings.Result.Length);
        var visible = readings.Result.AsEnumerable();
        if (selected is not null && first.Result.TryGetValue(selected.Id, out var before) && before.Date("reading_date") < b.From)
            visible = visible.Prepend(before);
        var series = visible.OrderBy(row => row.Date("reading_date")).ToArray();
        b.Chart("DashboardOdometer", "line", Km, series.Select(row =>
            (Label(ResourceReader.BoundsDate(row.Date("reading_date"))), (decimal?)row.Decimal("mileage"))));
        b.Table("DashboardOdometer", ["FieldDate", "Mileage"], series.Select(row => new object?[] { row.Date("reading_date"), row.Int("mileage") }));
        b.Chart("DashboardMileageRanking", "bar", Km, bounds.Where(row => row.Distance is not null)
            .Select(row => (Label(row.Vehicle.Plate, row.Vehicle.Id), row.Distance)), "vehicle");
        b.Table("DashboardMileageRanking", ["Vehicle", "DashboardDistance", "DashboardStartReading", "DashboardEndReading"],
            bounds.Select(row => new object?[] { row.Vehicle.Plate, row.Distance, row.Start, row.End }));
    }

    private sealed record MileageItem(FleetRow Vehicle, DateOnly? Start, DateOnly? End, decimal? Distance);

    private async Task<Dictionary<int, BusinessRow>> ReadBounds(FleetRow[] rows, DateOnly date, CancellationToken ct)
    {
        var result = new Dictionary<int, BusinessRow>();
        foreach (var batch in rows.Select(row => ResourceReader.Id(row.Id)).Chunk(150))
        {
            var readings = await _reader.RepresentativesAsync("vehicle_mileage_log", "vehicle_id,reading_date,mileage", "reading_date",
                "vehicle_id", ct, $"reading_date:lte:{ResourceReader.BoundsDate(date)}", $"vehicle_id:in:{string.Join(',', batch)}");
            foreach (var row in readings)
                result[row.Int("vehicle_id")] = row;
        }
        return result;
    }

    private async Task Repairs(Build b, CancellationToken ct)
    {
        var rows = await _reader.AllAsync("repair", "date,cost,vehicle_id", ct, b.Period("date", ("brigade_id", "brigade"), ("vehicle_id", "vehicle")));
        var vehicles = await _reader.MatchingAsync("vehicle", "registration_number", "id", rows.Select(row => row.Int("vehicle_id")), ct);
        var names = vehicles.ToDictionary(row => row.Id, row => row.Text("registration_number"));
        b.Card("Repair", rows.Length);
        b.Card("DashboardRepairCost", rows.Sum(row => row.Decimal("cost")), Cost);
        b.Card("DashboardAverageCost", rows.Length == 0 ? null : rows.Average(row => row.Decimal("cost")), Cost);
        b.Trend("DashboardMonthlyRepairs", "bar", Count, rows, "month", _ => 1);
        b.Trend("DashboardMonthlyCost", "bar", Cost, rows, "month", row => row.Decimal("cost"));
        b.Chart("DashboardRepairRanking", "bar", Cost, rows.GroupBy(row => row.Int("vehicle_id"))
            .Select(group => (Label(OrId(names, group.Key), group.Key), (decimal?)group.Sum(row => row.Decimal("cost"))))
            .OrderByDescending(pair => pair.Item2), "vehicle");
    }

    private async Task Teams(Build b, CancellationToken ct)
    {
        var rows = await _reader.AllAsync("repair", "date,work_volume,brigade_id", ct, b.Period("date", ("brigade_id", "brigade")));
        var ops = await _reader.MatchingAsync("component_operation", "repair_id,component_id,operation_type", "repair_id",
            rows.Select(row => row.Id), ct, b.Where(("component_id", "component")));
        var matched = ops.Select(row => row.Int("repair_id")).ToHashSet();
        if (b.Value("component") is not null)
            rows = rows.Where(row => matched.Contains(row.Id)).ToArray();
        var crews = _reader.MatchingAsync("brigade", "number,name", "id", rows.Select(row => row.Int("brigade_id")), ct);
        var parts = _reader.MatchingAsync("component", "name", "id", ops.Select(row => row.Int("component_id")), ct);
        await Task.WhenAll(crews, parts);
        var crewNames = crews.Result.ToDictionary(row => row.Id, Named);
        var partNames = parts.Result.ToDictionary(row => row.Id, row => row.Text("name"));
        var ranks = rows.GroupBy(row => row.Int("brigade_id")).OrderByDescending(group => group.Count())
            .ThenBy(group => OrId(crewNames, group.Key)).ToArray();
        var operationCounts = ops.GroupBy(row => (Part: OrId(partNames, row.Int("component_id")), Type: row.Text("operation_type")))
            .ToDictionary(group => group.Key, group => group.Count());
        b.Card("Repair", rows.Length);
        b.Card("Brigade", ranks.Length);
        b.Card("DashboardOperationCount", ops.Length);
        b.Chart("DashboardRepairsByTeam", "bar", Count, ranks.Select(group =>
            (Label(OrId(crewNames, group.Key), group.Key), (decimal?)group.Count())), "brigade");
        b.Matrix("DashboardOperationsByPart", "stack", operationCounts.Keys.Select(key => key.Part).Distinct().Order().ToArray(),
            ["ремонт", "замена"], key => key == "ремонт" ? "Enumoperationtype1" : "Enumoperationtype0",
            (part, type) => operationCounts.GetValueOrDefault((part, type)));
        b.Table("DashboardTeamWork", ["Brigade", "Repair", "FieldWorkVolume"], ranks.Select(group =>
            new object?[] { OrId(crewNames, group.Key), group.Count(), group.Sum(row => row.Decimal("work_volume")) }));
    }

    private async Task Staff(Build b, CancellationToken ct)
    {
        var rows = await _reader.AllAsync("employment_record", "date,hr_event_type,department_id", ct,
            b.Period("date", ("department_id", "department"), ("hr_event_type", "event")));
        var departments = await _reader.MatchingAsync("department", "name", "id", rows.Select(row => row.Int("department_id")), ct);
        var names = departments.ToDictionary(row => row.Id, row => row.Text("name"));
        var events = rows.GroupBy(row => (Month: ResourceReader.BoundsDate(Anchor(row.Date("date"), "month")), Event: row.Text("hr_event_type")))
            .ToDictionary(group => group.Key, group => group.Count());
        foreach (var (kind, title) in Events.Zip(EventTitles))
            b.Card(title, events.Where(pair => pair.Key.Event == kind).Sum(pair => pair.Value));
        var months = rows.Length == 0 ? [] : b.Days.Select(day => ResourceReader.BoundsDate(Anchor(day, "month"))).Distinct().ToArray();
        b.Matrix("DashboardMonthlyEvents", "group", months, Events, kind => EventTitles[Array.IndexOf(Events, kind)],
            (month, kind) => events.GetValueOrDefault((month, kind)));
        b.Chart("DashboardDepartmentRanking", "bar", Count, rows.GroupBy(row => row.Int("department_id"))
            .Select(group => (Label(OrId(names, group.Key), group.Key), (decimal?)group.Count())).OrderByDescending(pair => pair.Item2), "department");
    }
}
