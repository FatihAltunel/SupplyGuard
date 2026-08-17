namespace SupplyGuard.Application.Security;

public static class Permissions
{
    public const string ClaimType = "permission";

    public const string SuppliersRead = "suppliers.read";
    public const string SuppliersCreate = "suppliers.create";
    public const string SuppliersUpdate = "suppliers.update";
    public const string SuppliersChangeStatus = "suppliers.change-status";
    public const string RiskAssessmentsRead = "risk-assessments.read";
    public const string RiskAssessmentsCreate = "risk-assessments.create";
    public const string EarlyWarningsManage = "early-warnings.manage";
    public const string IdentityManage = "identity.manage";

    public static IReadOnlyCollection<string> All { get; } =
    [
        SuppliersRead,
        SuppliersCreate,
        SuppliersUpdate,
        SuppliersChangeStatus,
        RiskAssessmentsRead,
        RiskAssessmentsCreate,
        EarlyWarningsManage,
        IdentityManage
    ];
}
