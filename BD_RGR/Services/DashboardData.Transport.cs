using BD_RGR.Models;

namespace BD_RGR.Services;

internal sealed partial class DashboardData
{
    private async Task Overview(Build b, CancellationToken ct)
    {
        var trips = _reader.AllAsync("trip", "date,passenger_count", ct, b.Period("date"));
        var shipments = _reader.AllAsync("cargo_transportation", "date", ct, b.Period("date"));
        var repairs = _reader.AllAsync("repair", "date,cost", ct, b.Period("date"));
        var vehicles = _reader.AllAsync("vehicle", "status", ct);
        await Task.WhenAll(trips, shipments, repairs, vehicles);
        var statuses = vehicles.Result.GroupBy(row => row.Text("status")).ToDictionary(group => group.Key, group => group.Count());
        b.Card("DashboardPeople", trips.Result.Sum(row => row.Decimal("passenger_count")), People, "passengers");
        b.Card("DashboardTripCount", trips.Result.Length, Count, "routes");
        b.Card("DashboardShipmentCount", shipments.Result.Length, Count, "freight");
        b.Card("DashboardRepairCost", repairs.Result.Sum(row => row.Decimal("cost")), Cost, "repairs");
        b.Card("InRepair", statuses.GetValueOrDefault("в ремонте"), Count, "fleet", "status", "в ремонте");
        b.Trend("DashboardWeeklyPassengers", "line", People, trips.Result, "week", row => row.Decimal("passenger_count"));
        b.Trend("DashboardWeeklyShipments", "bar", Count, shipments.Result, "week", _ => 1);
        b.Trend("DashboardMonthlyCost", "bar", Cost, repairs.Result, "month", row => row.Decimal("cost"));
        b.Chart("DashboardStatusChart", "donut", Count, Statuses.Select(status =>
            (Label(StatusTitle(status), localized: true), (decimal?)statuses.GetValueOrDefault(status))));
    }

    private async Task Passenger(Build b, bool routesScreen, CancellationToken ct)
    {
        var trips = await _reader.AllAsync("trip", "date,route_id,passenger_vehicle_id,passenger_count", ct,
            b.Period("date", ("route_id", "route")));
        var selected = b.Value("route") is not null;
        var routes = await _reader.MatchingAsync("route", "number,name", "id", trips.Select(trip => trip.Int("route_id")), ct);
        var names = routes.ToDictionary(route => route.Id, Named);
        var stats = trips.GroupBy(row => row.Int("route_id"))
            .Select(group => new
            {
                Id = group.Key,
                Name = OrId(names, group.Key),
                Trips = group.Count(),
                People = group.Sum(row => row.Decimal("passenger_count")),
                Cars = group.Select(row => row.Int("passenger_vehicle_id")).Distinct().Count()
            }).ToArray();
        var passengers = trips.Sum(row => row.Decimal("passenger_count"));
        if (routesScreen)
        {
            var ranked = stats.OrderByDescending(row => row.Trips).ThenBy(row => row.Name).ToArray();
            b.Card("DashboardActiveRoutes", stats.Length);
            b.Card("DashboardTripCount", trips.Length);
            b.Chart("DashboardRouteFrequency", "bar", Count, ranked.Select(row => (Label(row.Name, row.Id), (decimal?)row.Trips)), "route");
            b.Chart("DashboardAveragePassengers", "bar", People, ranked.Select(row => (Label(row.Name, row.Id), (decimal?)(row.People / row.Trips))), "route");
            if (!selected)
            {
                b.Table("DashboardRouteSummary", ["Route", "DashboardTripCount", "DashboardPeople", "DashboardAveragePassengers", "DashboardVehicleCount"],
                    ranked.Select(row => new object?[] { row.Name, row.Trips, row.People, row.People / row.Trips, row.Cars }));
                return;
            }
        }
        else
        {
            b.Card("DashboardPeople", passengers, People);
            b.Card("DashboardTripCount", trips.Length);
            b.Card("DashboardAveragePassengers", trips.Length == 0 ? null : passengers / trips.Length, People);
            b.Trend("PassengersByDay", "line", People, trips, "day", row => row.Decimal("passenger_count"));
            if (!selected)
            {
                var ranked = stats.OrderByDescending(row => row.People).ThenBy(row => row.Name).ToArray();
                b.Chart("DashboardPassengerRanking", "bar", People, ranked.Select(row => (Label(row.Name, row.Id), (decimal?)row.People)), "route");
                b.Table("DashboardPassengerRanking", ["Route", "DashboardPeople"], ranked.Select(row => new object?[] { row.Name, row.People }));
                return;
            }
        }

        var buses = await _reader.MatchingAsync("passenger_vehicle", "registration_number", "id",
            trips.Select(trip => trip.Int("passenger_vehicle_id")), ct);
        var plates = buses.ToDictionary(row => row.Id, row => row.Text("registration_number"));
        if (routesScreen)
            b.Table("DashboardRouteVehicles", ["Vehicle", "DashboardTripCount"], trips.GroupBy(row => row.Int("passenger_vehicle_id"))
                .OrderByDescending(group => group.Count()).ThenBy(group => OrId(plates, group.Key))
                .Select(group => new object?[] { OrId(plates, group.Key), group.Count() }));
        else
            b.Table("DashboardRouteTrips", ["FieldDate", "Vehicle", "FieldPassengerCount"], trips.OrderByDescending(row => row.Date("date"))
                .ThenByDescending(row => row.Id).Select(row => new object?[] { row.Date("date"), OrId(plates, row.Int("passenger_vehicle_id")), row.Int("passenger_count") }));
    }

    private async Task Freight(Build b, CancellationToken ct)
    {
        var shipments = await _reader.AllAsync("cargo_transportation", "date,truck_id", ct, b.Period("date", ("truck_id", "truck")));
        var items = await _reader.MatchingAsync("transported_cargo", "cargo_transportation_id,cargo_id,quantity",
            "cargo_transportation_id", shipments.Select(row => row.Id), ct);
        var cargo = await _reader.MatchingAsync("cargo", "name,mass,cargo_type_id", "id", items.Select(row => row.Int("cargo_id")),
            ct, b.Where(("cargo_type_id", "cargoType")));
        var shipmentById = shipments.ToDictionary(row => row.Id);
        var cargoById = cargo.ToDictionary(row => row.Id);
        var positions = items.Where(item => cargoById.ContainsKey(item.Int("cargo_id")))
            .Select(item => new CargoPosition(item, shipmentById[item.Int("cargo_transportation_id")], cargoById[item.Int("cargo_id")])).ToArray();
        var matches = b.Value("cargoType") is null ? shipments : positions.Select(position => position.Shipment).DistinctBy(row => row.Id).ToArray();
        var kinds = _reader.MatchingAsync("cargo_type", "name", "id", cargo.Select(row => row.Int("cargo_type_id")), ct);
        var trucks = TruckNames(shipments, ct);
        await Task.WhenAll(kinds, trucks);
        var names = kinds.Result.ToDictionary(row => row.Id, row => row.Text("name"));

        b.Card("DashboardShipmentCount", matches.Length);
        b.Card("DashboardTruckCount", matches.Select(row => row.Int("truck_id")).Distinct().Count());
        b.Card("DashboardCargoMass", positions.Sum(position => position.Mass), Mass);
        b.Trend("DashboardWeeklyShipments", "bar", Count, matches, "week", _ => 1);
        b.Chart("DashboardMassByType", "donut", Mass, positions.GroupBy(position => position.Type)
            .Select(group => (Label(OrId(names, group.Key), group.Key), (decimal?)group.Sum(position => position.Mass)))
            .OrderByDescending(value => value.Item2), "cargoType");
        b.Table("DashboardRecentCargo", ["FieldDate", "Truck", "Cargo", "CargoType", "FieldQuantity", "DashboardCargoMass"],
            positions.OrderByDescending(position => position.Shipment.Date("date")).ThenByDescending(position => position.Item.Id)
                .Take(20).Select(position => new object?[] { position.Shipment.Date("date"),
                    OrId(trucks.Result,position.Shipment.Int("truck_id")), position.Cargo.Text("name"), OrId(names,position.Type),
                    position.Item.Int("quantity"), position.Mass }));
    }

    private sealed record CargoPosition(BusinessRow Item, BusinessRow Shipment, BusinessRow Cargo)
    {
        public int Type => Cargo.Int("cargo_type_id");
        public decimal Mass => Item.Int("quantity") * Cargo.Decimal("mass");
    }

    private async Task Trucks(Build b, CancellationToken ct)
    {
        var shipments = await _reader.AllAsync("cargo_transportation", "date,truck_id", ct, b.Period("date", ("truck_id", "truck")));
        var names = await TruckNames(shipments, ct);
        var groups = shipments.GroupBy(row => row.Int("truck_id")).OrderByDescending(group => group.Count())
            .ThenBy(group => OrId(names, group.Key)).ToArray();
        var exact = b.Value("grouping") != "weekdays";
        var labels = exact ? b.Days.Select(date => Label(ResourceReader.BoundsDate(date))).ToArray() :
            Enumerable.Range(1, 7).Select(day => Label("DashboardWeekday" + day, localized: true)).ToArray();

        b.Card("DashboardShipmentCount", shipments.Length);
        b.Card("DashboardActiveTrucks", groups.Length);
        b.Tables.Add(new("DashboardTruckHeat", [Label("Truck", localized: true), .. labels], groups.Select(group =>
        {
            var buckets = group.GroupBy(row => exact ? row.Date("date").DayNumber - b.From.DayNumber :
                ((int)row.Date("date").DayOfWeek + 6) % 7).ToDictionary(bucket => bucket.Key, bucket => bucket.Count());
            return new object?[] { OrId(names, group.Key) }.Concat(Enumerable.Range(0, labels.Length)
                .Select(index => (object)buckets.GetValueOrDefault(index))).ToArray();
        }).ToArray(), true));
        b.Chart("DashboardTruckRanking", "bar", Count, groups.Select(group => (Label(OrId(names, group.Key), group.Key), (decimal?)group.Count())), "truck");
        b.Table("DashboardTruckDays", ["Truck", "DashboardShipmentCount", "DashboardDays"], groups.Select(group =>
            new object?[] { OrId(names, group.Key), group.Count(), group.Select(row => row.Date("date")).Distinct().Count() }));
    }

    private async Task<Dictionary<int, string>> TruckNames(BusinessRow[] shipments, CancellationToken ct) =>
        (await _reader.MatchingAsync("truck", "registration_number", "id", shipments.Select(row => row.Int("truck_id")), ct))
            .ToDictionary(row => row.Id, row => row.Text("registration_number"));
}
