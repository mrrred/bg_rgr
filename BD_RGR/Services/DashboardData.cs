using BD_RGR.Models;

namespace BD_RGR.Services;

internal sealed partial class DashboardData(AuthApiClient api, Session session)
{
    private const string Count = "DashboardCountUnit";
    private const string People = "DashboardPeopleUnit";
    private const string Cost = "DashboardMoneyUnit";
    private const string Mass = "DashboardMassUnit";
    private const string Km = "DashboardKmUnit";
    private static readonly string[] Statuses = ["исправен", "в ремонте", "неисправен", "списан"];
    private static readonly string[] Events = ["прием", "перевод", "увольнение"];
    private static readonly string[] EventTitles = ["Enumhreventtype0", "Enumhreventtype2", "Enumhreventtype1"];
    private static readonly (string Resource, string Category, string Title)[] VehicleTypes =
    [
        ("passenger_vehicle", "passenger", "DashboardPassengerCategory"),
        ("truck", "freight", "DashboardFreightCategory"),
        ("auxiliary_vehicle", "auxiliary", "DashboardAuxCategory")
    ];

    private static readonly (DashboardScreen Screen, string[] Resources)[] Catalog =
    [
        (new("overview", "DashboardSummary", "DashboardOverview", [], true), ["trip","cargo_transportation","repair","vehicle"]),
        (new("passengers", "DashboardPassengerGroup", "DashboardPassengers", ["route"], true), ["trip","route","passenger_vehicle"]),
        (new("routes", "DashboardPassengerGroup", "DashboardRoutes", ["route"], true), ["trip","route","passenger_vehicle"]),
        (new("freight", "DashboardFreightGroup", "DashboardFreight", ["truck","cargoType"], true), ["cargo_transportation","truck","transported_cargo","cargo","cargo_type"]),
        (new("trucks", "DashboardFreightGroup", "DashboardTrucks", ["truck","grouping"], true), ["cargo_transportation","truck"]),
        (new("fleet", "DashboardGarageGroup", "DashboardFleet", ["garage","category","status"], false), ["vehicle","garage","passenger_vehicle","truck","auxiliary_vehicle"]),
        (new("mileage", "DashboardGarageGroup", "DashboardMileage", ["garage","category","vehicle"], true), ["vehicle","garage","passenger_vehicle","truck","auxiliary_vehicle","vehicle_mileage_log"]),
        (new("repairs", "DashboardRepairGroup", "DashboardRepairs", ["vehicle","brigade"], true), ["repair","vehicle","brigade"]),
        (new("teams", "DashboardRepairGroup", "DashboardTeams", ["brigade","component"], true), ["repair","brigade","component_operation","component"]),
        (new("staff", "DashboardStaffGroup", "DashboardStaff", ["department","event"], true), ["employment_record","department"])
    ];

    private static readonly Dictionary<string, (string Table, string Fields, string Label)> Lookups = new()
    {
        ["route"] = ("route", "number,name", "name"),
        ["truck"] = ("truck", "registration_number", "registration_number"),
        ["vehicle"] = ("vehicle", "registration_number", "registration_number"),
        ["garage"] = ("garage", "number,name", "name"),
        ["brigade"] = ("brigade", "number,name", "name"),
        ["cargoType"] = ("cargo_type", "name", "name"),
        ["department"] = ("department", "name", "name"),
        ["component"] = ("component", "name", "name")
    };

    private readonly ResourceReader _reader = new(api, session);

    internal async Task<DashboardScreen[]> ScreensAsync(CancellationToken ct)
    {
        var resources = (await api.ResourcesAsync(session, ct)).Where(resource => resource.Rights.Contains('R'))
            .Select(resource => resource.Name).ToHashSet();
        return Catalog.Where(entry => entry.Resources.All(resources.Contains)).Select(entry => entry.Screen).ToArray();
    }

    internal async Task<Dictionary<string, DashboardOption[]>> OptionsAsync(DashboardScreen screen, CancellationToken ct)
    {
        var options = await Task.WhenAll(screen.Filters.Select(async name => (name, await OptionsFor(screen.Name, name, ct))));
        return options.ToDictionary(option => option.name, option => option.Item2);
    }

    private async Task<DashboardOption[]> OptionsFor(string screen, string filter, CancellationToken ct)
    {
        if (filter == "vehicle" && screen == "mileage")
            return (await FleetRows([], ct)).Select(row => new DashboardOption(ResourceReader.Id(row.Id), row.Plate, false,
                row.Garage, row.Category)).ToArray();

        switch (filter)
        {
            case "category": return VehicleTypes.Select(type => Option(type.Category, type.Title, true)).ToArray();
            case "status": return Statuses.Select(status => Option(status, StatusTitle(status), true)).ToArray();
            case "event": return Events.Zip(EventTitles, (value, key) => Option(value, key, true)).ToArray();
            case "grouping": return [Option("dates", "DashboardByDate", true), Option("weekdays", "DashboardByWeekday", true)];
        }
        var lookup = Lookups[filter];
        return (await _reader.AllAsync(lookup.Table, lookup.Fields, ct))
            .Select(row => Option(ResourceReader.Id(row.Id), row.Value("number") is { } number
                ? $"{number} · {row.Text(lookup.Label)}" : row.Text(lookup.Label)))
            .OrderBy(option => option.Label, StringComparer.CurrentCulture).ToArray();
    }

    internal async Task<DashboardResult> LoadAsync(string name, DashboardRequest request, CancellationToken ct)
    {
        var build = new Build(request);
        await (name switch
        {
            "overview" => Overview(build, ct),
            "passengers" => Passenger(build, false, ct),
            "routes" => Passenger(build, true, ct),
            "freight" => Freight(build, ct),
            "trucks" => Trucks(build, ct),
            "fleet" => Fleet(build, ct),
            "mileage" => Mileage(build, ct),
            "repairs" => Repairs(build, ct),
            "teams" => Teams(build, ct),
            "staff" => Staff(build, ct),
            _ => throw new AuthApiException("RowNotFound")
        });
        return new(DateTimeOffset.UtcNow, build.Cards.ToArray(), build.Charts.ToArray(), build.Tables.ToArray(), build.Vehicle);
    }

    private static DashboardOption Option(string value, string label, bool localized = false) =>
        new(value, label, localized, null, null);

    private static string StatusTitle(string status) => "Enumvehiclestatus" + Array.IndexOf(Statuses, status);
    private static string CategoryTitle(string category) => VehicleTypes.FirstOrDefault(type => type.Category == category).Title
        ?? "DashboardUnknownCategory";
    private static string Named(BusinessRow row) => $"{row.Text("number")} · {row.Text("name")}";
    private static string OrId(IReadOnlyDictionary<int, string> names, int id) => names.GetValueOrDefault(id) ?? $"#{id}";
    private static DateOnly Anchor(DateOnly date, string cadence) => cadence switch
    {
        "week" => date.AddDays(-(((int)date.DayOfWeek + 6) % 7)),
        "month" => new(date.Year, date.Month, 1),
        _ => date
    };

    private sealed class Build(DashboardRequest request)
    {
        public DateOnly From => request.From!.Value;
        public DateOnly To => request.To!.Value;
        public List<DashboardCard> Cards { get; } = [];
        public List<DashboardPlot> Charts { get; } = [];
        public List<DashboardTable> Tables { get; } = [];
        public string? Vehicle { get; set; }
        public string? Value(string filter) => request.Values.GetValueOrDefault(filter);

        public string[] Where(params (string Field, string Filter)[] fields) => fields
            .Where(field => Value(field.Filter) is not null).Select(field => $"{field.Field}:eq:{Value(field.Filter)}").ToArray();

        public string[] Period(string date, params (string Field, string Filter)[] fields) =>
            [$"{date}:gte:{ResourceReader.BoundsDate(From)}", $"{date}:lte:{ResourceReader.BoundsDate(To)}", .. Where(fields)];

        public DateOnly[] Days => Enumerable.Range(0, To.DayNumber - From.DayNumber + 1).Select(From.AddDays).ToArray();

        public void Card(string title, decimal? value, string unit = Count, string? target = null, string? filter = null, string? choice = null) =>
            Cards.Add(new(title, value, unit, target, filter, choice, null, null));

        public void Table(string title, string[] headings, IEnumerable<object?[]> rows) =>
            Tables.Add(new(title, headings.Select(key => new DashboardLabel(key, null, true)).ToArray(), rows.ToArray(), false));

        public void Chart(string title, string kind, string unit, IEnumerable<(DashboardLabel Label, decimal? Value)> items,
            string? filter = null)
        {
            var values = items.ToArray();
            Charts.Add(new(title, kind, unit, values.Select(item => item.Label).ToArray(),
                [new(null, values.Select(item => item.Value).ToArray())], filter));
        }

        public void Trend(string title, string kind, string unit, IEnumerable<BusinessRow> rows, string cadence,
            Func<BusinessRow, decimal> value)
        {
            var totals = rows.GroupBy(row => Anchor(row.Date("date"), cadence)).ToDictionary(group => group.Key, group => group.Sum(value));
            var keys = totals.Count == 0 ? [] : Days.Select(date => Anchor(date, cadence)).Distinct().ToArray();
            Chart(title, kind, unit, keys.Select(date => (Label(ResourceReader.BoundsDate(date)), (decimal?)totals.GetValueOrDefault(date))));
        }

        public void Matrix(string title, string kind, string[] axes, string[] series, Func<string, string> titleFor,
            Func<string, string, decimal> count) =>
            Charts.Add(new(title, kind, Count, axes.Select(axis => Label(axis)).ToArray(),
                series.Select(key => new DashboardSeries(titleFor(key), axes.Select(axis => (decimal?)count(axis, key)).ToArray())).ToArray(), null));
    }

    private static DashboardLabel Label(string text, int? id = null, bool localized = false) =>
        new(text, id is { } value ? ResourceReader.Id(value) : null, localized);
}
