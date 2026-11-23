namespace SupplyGuard.Application.Security;

public static class Roles
{
    public const string Administrator = "Administrator";

    /// <summary>Shared anonymous demo account: read suppliers, request and read risk analyses only.</summary>
    public const string DemoUser = "DemoUser";

    public static IReadOnlyCollection<string> DemoUserPermissions { get; } =
    [
        Permissions.SuppliersRead,
        Permissions.RiskAssessmentsRead,
        Permissions.RiskAssessmentsCreate
    ];
}
