using BD_RGR.Models;

namespace BD_RGR.ViewModels;

internal static class WorkspaceMenu
{
    private static readonly string[] BusinessTables =
        ["MileageLog", "Route", "Trip", "CargoTransportation", "Employee", "EmploymentRecord", "Brigade", "Repair", "VehiclesRequiringRepair"];

    private static readonly string[] References =
        ["Garage", "Brand", "Model", "Cargo", "CargoType", "Point", "Component", "City", "Street",
         "JobPosition", "Profession", "Specialty", "Qualification", "Workplace", "Department"];

    private static readonly string[] Relations =
        ["TransportedCargo", "IntermediatePoint", "RouteAssignment", "VehicleAssignment", "BrigadeMembership", "ComponentOperation"];

    public static IReadOnlyList<MenuGroup> CreateGroups(Role role, Action<MenuEntry> activate)
    {
        var allTables = role == Role.Director || role == Role.Reader;
        var allowed = role.Code switch
        {
            "pass_dispatcher" => new[] { "Route", "Trip", "IntermediatePoint", "RouteAssignment", "Point", "Model", "Brand", "Garage" },
            "cargo_dispatcher" => ["CargoTransportation", "TransportedCargo", "Cargo", "CargoType", "Model", "Brand", "Garage"],
            "garage_engineer" => ["MileageLog", "Model", "Brand", "Garage"],
            _ => []
        };

        bool CanView(string key) => allTables || allowed.Contains(key);
        MenuEntry Entry(string key) => new(key, activate, isResource: true);
        var tables = new List<MenuEntry>();

        if (allTables || role == Role.GarageEngineer || role == Role.PassengerDispatcher)
            tables.Add(Entry("PassengerVehicle"));
        if (allTables || role == Role.GarageEngineer || role == Role.CargoDispatcher)
            tables.Add(Entry("Truck"));
        if (allTables || role == Role.GarageEngineer)
            tables.Add(Entry("AuxiliaryVehicle"));

        tables.AddRange(BusinessTables.Where(CanView).Select(Entry));

        return
        [
            new("Tables", tables, []),
            new("Directories", References.Where(CanView).Select(Entry).ToArray(),
                Relations.Where(CanView).Select(Entry).ToArray())
        ];
    }

    public static IReadOnlyList<MenuEntry> CreatePages(Role role, Action<MenuEntry> activate)
    {
        string[] reports = role.Code switch
        {
            "pass_dispatcher" => ["RouteReport"],
            "cargo_dispatcher" => ["CargoReport"],
            "garage_engineer" => ["MileageReport"],
            _ => ["RouteReport", "CargoReport", "MileageReport"]
        };

        return
        [
            new("Documents", activate, children: [.. reports, "ManualReport"]),
            new("Analytics", activate),
            new("Help", activate, children: ["UserGuide", "About"]),
            new("Miscellaneous", activate, children: ["Settings", "ChangePassword"])
        ];
    }
}

public sealed record MenuGroup(string TitleKey, IReadOnlyList<MenuEntry> Entries, IReadOnlyList<MenuEntry> RelatedEntries)
{
    public bool ExpandedByDefault => TitleKey == "Tables";
    public bool HasRelatedEntries => RelatedEntries.Count > 0;
}

public sealed class MenuEntry : ViewModel
{
    private bool _isActive;

    public MenuEntry(string titleKey, Action<MenuEntry> activate, bool isResource = false, string[]? children = null)
    {
        TitleKey = titleKey;
        IsResource = isResource;
        Activate = new Command(() => activate(this));
        Children = children?.Select(key => new MenuEntry(key, activate) { Parent = this }).ToArray() ?? [];
    }

    public string TitleKey { get; }
    public bool IsResource { get; }
    public Command Activate { get; }
    public IReadOnlyList<MenuEntry> Children { get; }
    public MenuEntry? Parent { get; private init; }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }
}
