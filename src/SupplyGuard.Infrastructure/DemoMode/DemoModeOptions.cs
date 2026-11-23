namespace SupplyGuard.Infrastructure.DemoMode;

/// <summary>
/// Explicit, environment-independent demo switch (e.g. a public demo on a Production server). When enabled:
/// simulated supplier integrations, caller-supplied feature overrides, demo suppliers, and a shared
/// passwordless demo account reachable through POST /api/auth/demo. Never enable it for real supplier data.
/// </summary>
public sealed class DemoModeOptions
{
    public const string SectionName = "DemoMode";
    public const string DemoUserName = "demo";
    public const string DemoUserEmail = "demo@supplyguard.local";

    public bool Enabled { get; set; }
}
