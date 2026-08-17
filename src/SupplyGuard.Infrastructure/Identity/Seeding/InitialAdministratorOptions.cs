namespace SupplyGuard.Infrastructure.Identity.Seeding;

public sealed class InitialAdministratorOptions
{
    public const string SectionName = "Identity:InitialAdministrator";

    public string UserName { get; init; } = null!;
    public string Email { get; init; } = null!;
    public string Password { get; init; } = null!;
}
