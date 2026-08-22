using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure.Identity.Entities;

namespace SupplyGuard.Infrastructure.Identity.Seeding;

public sealed class IdentitySeeder(
    RoleManager<AppRole> roleManager,
    UserManager<AppUser> userManager,
    IOptions<InitialAdministratorOptions> options)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var initialAdministrator = options.Value;
        ValidateConfiguration(initialAdministrator);

        var administratorRole = await roleManager.FindByNameAsync(Roles.Administrator);
        if (administratorRole is null)
        {
            var createRoleResult = await roleManager.CreateAsync(new AppRole { Name = Roles.Administrator });
            EnsureSucceeded(createRoleResult, "create the Administrator role");
            administratorRole = await roleManager.FindByNameAsync(Roles.Administrator)
                ?? throw new InvalidOperationException("Administrator role could not be loaded after creation.");
        }

        var existingPermissionClaims = await roleManager.GetClaimsAsync(administratorRole);
        foreach (var permission in Permissions.All)
        {
            if (existingPermissionClaims.Any(claim =>
                    claim.Type == Permissions.ClaimType &&
                    claim.Value == permission))
            {
                continue;
            }

            var addClaimResult = await roleManager.AddClaimAsync(
                administratorRole,
                new System.Security.Claims.Claim(Permissions.ClaimType, permission));
            EnsureSucceeded(addClaimResult, $"assign permission '{permission}' to the Administrator role");
        }

        var administrator = await userManager.FindByNameAsync(initialAdministrator.UserName)
            ?? await userManager.FindByEmailAsync(initialAdministrator.Email);

        if (administrator is null)
        {
            administrator = new AppUser
            {
                UserName = initialAdministrator.UserName,
                Email = initialAdministrator.Email,
                EmailConfirmed = true
            };

            var createUserResult = await userManager.CreateAsync(administrator, initialAdministrator.Password);
            EnsureSucceeded(createUserResult, "create the initial Administrator user");
        }

        if (!await userManager.IsInRoleAsync(administrator, Roles.Administrator))
        {
            var addToRoleResult = await userManager.AddToRoleAsync(administrator, Roles.Administrator);
            EnsureSucceeded(addToRoleResult, "assign the Administrator role to the initial user");
        }
    }

    private static void ValidateConfiguration(InitialAdministratorOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.UserName) ||
            string.IsNullOrWhiteSpace(options.Email) ||
            string.IsNullOrWhiteSpace(options.Password) ||
            options.Password.Contains("<YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Identity:InitialAdministrator configuration must provide UserName, Email, and a non-placeholder " +
                "Password through User Secrets or environment variables.");
        }
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                $"Unable to {operation}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
        }
    }
}
