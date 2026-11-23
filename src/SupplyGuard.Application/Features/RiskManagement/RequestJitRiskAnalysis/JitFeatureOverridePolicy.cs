namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

/// <summary>
/// Whether callers may supply the three integration-derived features directly.
/// Disabled by default; only the explicit demo registration (simulated integrations) enables it.
/// </summary>
public sealed record JitFeatureOverridePolicy(bool Enabled)
{
    public static JitFeatureOverridePolicy Disabled { get; } = new(false);
    public static JitFeatureOverridePolicy DemoOnly { get; } = new(true);
}
