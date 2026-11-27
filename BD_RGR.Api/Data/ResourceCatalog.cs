namespace BD_RGR.Api.Data;

internal sealed record Field(string Name, string TitleKey, string Kind, bool Nullable = false,
    string? Target = null, int MaxLength = 0, string? EnumType = null, string? Default = null)
{
    public string Cast => Kind switch
    {
        "reference" or "integer" => "integer",
        "numeric" => "numeric",
        "date" => "date",
        "boolean" => "boolean",
        "enum" => $"public.{EnumType}",
        _ => "text"
    };

    public IReadOnlyList<EnumChoice>? Choices => EnumType is null ? null : ResourceCatalog.Enums[EnumType];
}

internal sealed record EnumChoice(string Value, string TitleKey, string English);
internal sealed record Resource(string Name, string TitleKey, Field[] Fields, bool IsVehicleSubtype = false)
{
    public IEnumerable<Field> DisplayFields => IsVehicleSubtype ? ResourceCatalog.Vehicle.Fields.Concat(Fields) : Fields;
    public IEnumerable<Field> ReadFields => IsVehicleSubtype ? DisplayFields.Append(VehicleId) : DisplayFields;
    private static readonly Field VehicleId = new("vehicle_id", "Vehicle", "reference", Target: "vehicle");
}

internal static class ResourceCatalog
{
    internal static readonly Resource Vehicle = new("vehicle", "Vehicle",
    [
            new("registration_number", "RegistrationNumber", "text", MaxLength: 9),
            new("model_id", "Model", "reference", Target: "model"),
            new("garage_id", "Garage", "reference", Target: "garage"),
            new("status", "Status", "enum", EnumType: "vehicle_status", Default: "исправен"),
            new("mileage", "Mileage", "integer", Default: "0"),
            new("color", "Color", "enum", EnumType: "vehicle_color"),
            new("engine_number", "EngineNumber", "text", MaxLength: 30),
            new("body_number", "BodyNumber", "text", MaxLength: 30),
            new("chassis_number", "ChassisNumber", "text", MaxLength: 30),
            new("manufacture_date", "ManufactureDate", "date"),
            new("inventory_number", "InventoryNumber", "text", MaxLength: 20),
            new("last_overhaul_date", "OverhaulDate", "date", Nullable: true)
    ]);

    internal static readonly Resource[] Tables =
    [
        new("brigade", "Brigade",
        [
            new("number", "FieldNumber", "text", MaxLength: 10),
            new("name", "FieldName", "text", MaxLength: 100),
            new("foreman_id", "FieldForeman", "reference", Nullable: true, Target: "employee")
        ]),
        new("employee", "Employee",
        [
            new("last_name", "FieldLastName", "text", MaxLength: 25),
            new("first_name", "FieldFirstName", "text", MaxLength: 25),
            new("middle_name", "FieldMiddleName", "text", MaxLength: 25),
            new("gender", "FieldGender", "enum", EnumType: "gender_type"),
            new("birth_date", "FieldBirthDate", "date"),
            new("work_experience", "FieldWorkExperience", "integer"),
            new("house", "FieldHouse", "text", MaxLength: 10),
            new("city_id", "City", "reference", Target: "city"),
            new("street_id", "Street", "reference", Target: "street")
        ]),
        new("brand", "Brand",
        [
            new("name", "FieldName", "text", MaxLength: 80)
        ]),
        new("model", "Model",
        [
            new("name", "FieldName", "text", MaxLength: 80),
            new("brand_id", "Brand", "reference", Target: "brand")
        ]),
        new("garage", "Garage",
        [
            new("number", "FieldNumber", "text", MaxLength: 10),
            new("name", "FieldName", "text", MaxLength: 80)
        ]),
        new("component", "Component",
        [
            new("name", "FieldName", "text", MaxLength: 100)
        ]),
        new("passenger_vehicle", "PassengerVehicle",
        [
            new("total_capacity", "Capacity", "integer"),
            new("seat_count", "Seats", "integer")
        ], IsVehicleSubtype: true),
        new("truck", "Truck",
        [
            new("load_capacity", "LoadCapacity", "numeric")
        ], IsVehicleSubtype: true),
        new("auxiliary_vehicle", "AuxiliaryVehicle",
        [
            new("usage_intensity", "UsageIntensity", "numeric")
        ], IsVehicleSubtype: true),
        new("point", "Point",
        [
            new("name", "FieldName", "text", MaxLength: 100)
        ]),
        new("route", "Route",
        [
            new("number", "FieldNumber", "text", MaxLength: 10),
            new("name", "FieldName", "text", MaxLength: 100),
            new("start_point_id", "FieldStartPoint", "reference", Target: "point"),
            new("end_point_id", "FieldEndPoint", "reference", Target: "point")
        ]),
        new("intermediate_point", "IntermediatePoint",
        [
            new("route_id", "Route", "reference", Target: "route"),
            new("point_id", "Point", "reference", Target: "point")
        ]),
        new("route_assignment", "RouteAssignment",
        [
            new("passenger_vehicle_id", "PassengerVehicle", "reference", Target: "passenger_vehicle"),
            new("route_id", "Route", "reference", Target: "route")
        ]),
        new("trip", "Trip",
        [
            new("date", "FieldDate", "date"),
            new("passenger_count", "FieldPassengerCount", "integer"),
            new("passenger_vehicle_id", "PassengerVehicle", "reference", Target: "passenger_vehicle"),
            new("route_id", "Route", "reference", Target: "route")
        ]),
        new("cargo_type", "CargoType",
        [
            new("name", "FieldName", "text", MaxLength: 100)
        ]),
        new("cargo", "Cargo",
        [
            new("name", "FieldName", "text", MaxLength: 100),
            new("mass", "FieldMass", "numeric"),
            new("volume", "FieldVolume", "numeric"),
            new("cargo_type_id", "CargoType", "reference", Target: "cargo_type")
        ]),
        new("cargo_transportation", "CargoTransportation",
        [
            new("date", "FieldDate", "date"),
            new("truck_id", "Truck", "reference", Target: "truck")
        ]),
        new("transported_cargo", "TransportedCargo",
        [
            new("quantity", "FieldQuantity", "integer"),
            new("cargo_transportation_id", "CargoTransportation", "reference", Target: "cargo_transportation"),
            new("cargo_id", "Cargo", "reference", Target: "cargo")
        ]),
        new("employment_record", "EmploymentRecord",
        [
            new("date", "FieldDate", "date"),
            new("hr_event_type", "FieldHrEventType", "enum", EnumType: "hr_event_type"),
            new("termination_reason", "FieldTerminationReason", "text", Nullable: true, MaxLength: 500),
            new("document_date", "FieldDocumentDate", "date"),
            new("document_number", "FieldDocumentNumber", "text", MaxLength: 30),
            new("document_type", "FieldDocumentType", "enum", EnumType: "document_type"),
            new("employee_id", "Employee", "reference", Target: "employee"),
            new("job_position_id", "JobPosition", "reference", Target: "job_position"),
            new("profession_id", "Profession", "reference", Target: "profession"),
            new("specialty_id", "Specialty", "reference", Target: "specialty"),
            new("qualification_id", "Qualification", "reference", Target: "qualification"),
            new("workplace_id", "Workplace", "reference", Target: "workplace"),
            new("department_id", "Department", "reference", Target: "department")
        ]),
        new("brigade_membership", "BrigadeMembership",
        [
            new("employee_id", "Employee", "reference", Target: "employee"),
            new("brigade_id", "Brigade", "reference", Target: "brigade")
        ]),
        new("vehicle_assignment", "VehicleAssignment",
        [
            new("employee_id", "Employee", "reference", Target: "employee"),
            new("vehicle_id", "Vehicle", "reference", Target: "vehicle")
        ]),
        new("city", "City",
        [
            new("name", "FieldName", "text", MaxLength: 100)
        ]),
        new("street", "Street",
        [
            new("name", "FieldName", "text", MaxLength: 150)
        ]),
        new("department", "Department",
        [
            new("name", "FieldName", "text", MaxLength: 150)
        ]),
        new("job_position", "JobPosition",
        [
            new("okpdtr_2025_code", "FieldOkpdtr2025Code", "text", MaxLength: 6),
            new("name", "FieldName", "text", MaxLength: 150)
        ]),
        new("profession", "Profession",
        [
            new("name", "FieldName", "text", MaxLength: 150)
        ]),
        new("qualification", "Qualification",
        [
            new("name", "FieldName", "text", MaxLength: 100)
        ]),
        new("specialty", "Specialty",
        [
            new("name", "FieldName", "text", MaxLength: 150)
        ]),
        new("workplace", "Workplace",
        [
            new("name", "FieldName", "text", MaxLength: 150)
        ]),
        new("repair", "Repair",
        [
            new("date", "FieldDate", "date"),
            new("cost", "FieldCost", "numeric"),
            new("work_volume", "FieldWorkVolume", "numeric"),
            new("vehicle_id", "Vehicle", "reference", Target: "vehicle"),
            new("brigade_id", "Brigade", "reference", Target: "brigade"),
            new("is_active", "FieldIsActive", "boolean", Default: "true")
        ]),
        new("component_operation", "ComponentOperation",
        [
            new("repair_id", "Repair", "reference", Target: "repair"),
            new("component_id", "Component", "reference", Target: "component"),
            new("operation_type", "FieldOperationType", "enum", EnumType: "operation_type")
        ]),
        new("vehicle_mileage_log", "MileageLog",
        [
            new("vehicle_id", "Vehicle", "reference", Target: "vehicle"),
            new("reading_date", "FieldDate", "date"),
            new("mileage", "Mileage", "integer")
        ]),
        new("vehicles_requiring_repair", "VehiclesRequiringRepair", Vehicle.Fields)
    ];

    internal static readonly IReadOnlyDictionary<string, Resource> Resources =
        Tables.Append(Vehicle).ToDictionary(table => table.Name);

    internal static readonly IReadOnlyDictionary<string, EnumChoice[]> Enums = new Dictionary<string, EnumChoice[]>
    {
        ["document_type"] = [new("приказ", "Enumdocumenttype0", "Order"), new("распоряжение", "Enumdocumenttype1", "Directive"), new("трудовой договор", "Enumdocumenttype2", "EmploymentContract")],
        ["gender_type"] = [new("м", "Enumgendertype0", "Male"), new("ж", "Enumgendertype1", "Female")],
        ["hr_event_type"] = [new("прием", "Enumhreventtype0", "Hiring"), new("увольнение", "Enumhreventtype1", "Termination"), new("перевод", "Enumhreventtype2", "Transfer")],
        ["operation_type"] = [new("замена", "Enumoperationtype0", "Replacement"), new("ремонт", "Enumoperationtype1", "Repair")],
        ["vehicle_color"] = [new("белый", "Enumvehiclecolor0", "White"), new("черный", "Enumvehiclecolor1", "Black"), new("серый", "Enumvehiclecolor2", "Gray"), new("красный", "Enumvehiclecolor3", "Red"), new("синий", "Enumvehiclecolor4", "Blue"), new("зеленый", "Enumvehiclecolor5", "Green"), new("желтый", "Enumvehiclecolor6", "Yellow"), new("оранжевый", "Enumvehiclecolor7", "Orange")],
        ["vehicle_status"] = [new("исправен", "Enumvehiclestatus0", "Operational"), new("в ремонте", "Enumvehiclestatus1", "In repair"), new("неисправен", "Enumvehiclestatus2", "Faulty"), new("списан", "Enumvehiclestatus3", "Retired")]
    };

    internal static readonly IReadOnlyDictionary<string, string> UniqueFields = new Dictionary<string, string>
    {
        ["auxiliary_vehicle_vehicle_uq"] = "vehicle_id",
        ["brand_name_uq"] = "name",
        ["brigade_membership_employee_uq"] = "employee_id",
        ["brigade_number_uq"] = "number",
        ["cargo_type_name_uq"] = "name",
        ["city_name_uq"] = "name",
        ["component_name_uq"] = "name",
        ["component_operation_repair_component_type_uq"] = "repair_id",
        ["department_name_uq"] = "name",
        ["garage_number_uq"] = "number",
        ["intermediate_point_route_point_uq"] = "route_id",
        ["job_position_code_uq"] = "okpdtr_2025_code",
        ["model_brand_name_uq"] = "brand_id",
        ["passenger_vehicle_vehicle_uq"] = "vehicle_id",
        ["point_name_uq"] = "name",
        ["profession_name_uq"] = "name",
        ["qualification_name_uq"] = "name",
        ["route_assignment_vehicle_uq"] = "passenger_vehicle_id",
        ["route_number_uq"] = "number",
        ["specialty_name_uq"] = "name",
        ["street_name_uq"] = "name",
        ["transported_cargo_unique"] = "cargo_transportation_id",
        ["truck_vehicle_uq"] = "vehicle_id",
        ["vehicle_assignment_employee_vehicle_uq"] = "employee_id",
        ["vehicle_body_number_uq"] = "body_number",
        ["vehicle_chassis_number_uq"] = "chassis_number",
        ["vehicle_engine_number_uq"] = "engine_number",
        ["vehicle_inventory_uq"] = "inventory_number",
        ["vehicle_mileage_log_date_uq"] = "vehicle_id",
        ["vehicle_registration_uq"] = "registration_number",
        ["workplace_name_uq"] = "name"
    };

    internal static string Rights(string role, string table)
    {
        if (table == "vehicles_requiring_repair")
            return role is "director" or "reader" ? "R" : "";
        if (table == "vehicle")
            return role is "director" or "reader" or "garage_engineer" ? "R" : "";

        return role switch
        {
            "director" => "CRUD",
            "reader" => "R",
            "pass_dispatcher" => table switch
            {
                "passenger_vehicle" or "intermediate_point" or "route_assignment" => "CRUD",
                "route" or "trip" => "CRU",
                "brand" or "model" or "garage" or "point" => "R",
                _ => ""
            },
            "cargo_dispatcher" => table switch
            {
                "truck" or "cargo" or "cargo_transportation" or "transported_cargo" => "CRUD",
                "brand" or "model" or "garage" or "cargo_type" => "R",
                _ => ""
            },
            "garage_engineer" => table switch
            {
                "passenger_vehicle" or "truck" or "auxiliary_vehicle" or "vehicle_mileage_log" => "CRUD",
                "brand" or "model" or "garage" => "R",
                _ => ""
            },
            _ => ""
        };
    }

    internal static bool Can(string role, string table, char operation) => Rights(role, table).Contains(operation);
}
