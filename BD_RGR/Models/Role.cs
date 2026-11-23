namespace BD_RGR.Models;

public sealed record Role(string Code, string TitleKey)
{
    public static Role Director { get; } = new("director", "RoleDirector");
    public static Role PassengerDispatcher { get; } = new("pass_dispatcher", "RolePassenger");
    public static Role CargoDispatcher { get; } = new("cargo_dispatcher", "RoleCargo");
    public static Role GarageEngineer { get; } = new("garage_engineer", "RoleEngineer");
    public static Role Reader { get; } = new("reader", "RoleReader");

    public static IReadOnlyList<Role> All { get; } =
        [Director, PassengerDispatcher, CargoDispatcher, GarageEngineer, Reader];
}

public sealed record RegistrationRole(int Id, Role Role)
{
    public string TitleKey => Role.TitleKey;
}

public sealed class Session(string username, Role role, string token)
{
    public string Username { get; } = username;
    public Role Role { get; } = role;
    internal string Token { get; } = token;
}
