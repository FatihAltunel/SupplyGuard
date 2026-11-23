namespace SupplyGuard.Infrastructure.Security;

/// <summary>
/// Rejects configuration values that are not secret: "&lt;YOUR_...&gt;" placeholders and values that have
/// been committed to this public repository. Git history cannot be rewritten (protected main), so these
/// values are permanently public; a deployment that still uses one must fail at startup.
/// </summary>
public static class PublicSecrets
{
    private static readonly HashSet<string> Leaked = new(StringComparer.Ordinal)
    {
        // JWT signing keys from appsettings.Development.json and .env.example history.
        "_GcthTQLLqu66DxMGt9sY6lpu6fuHmlxbTBlBJPG9qkqQAyPo7b-7mKLqWkOUc3l",
        "development-only-signing-key-change-before-production-2026",
        "SuperSecretKeyForSupplyGuard_2026_TestEnvironment!",
        // Initial administrator passwords from the same files.
        "Sg!9aA-oNizVpOKGc-sLvjZq77_FvO4RFj049N-",
        "ChangeThis!DevelopmentAdmin2026",
        "SupplyGuard_Admin2026!"
    };

    /// <summary>True for placeholders and for values known to be published in this repository.</summary>
    public static bool IsUnsafe(string? value) =>
        value is not null &&
        (value.Contains("<YOUR_", StringComparison.OrdinalIgnoreCase) || Leaked.Contains(value.Trim()));
}
