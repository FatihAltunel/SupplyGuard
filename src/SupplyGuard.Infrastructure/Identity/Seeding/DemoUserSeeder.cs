using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure.DemoMode;
using SupplyGuard.Infrastructure.Identity.Entities;

namespace SupplyGuard.Infrastructure.Identity.Seeding;

/// <summary>
/// Creates the shared demo account used by POST /api/auth/demo. The account has no password, so it
/// cannot sign in through /api/auth/login, and its role is reset to exactly the demo permissions.
/// </summary>
public sealed class DemoUserSeeder(RoleManager<AppRole> roleManager, UserManager<AppUser> userManager)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var role = await roleManager.FindByNameAsync(Roles.DemoUser);
        if (role is null)
        {
            EnsureSucceeded(await roleManager.CreateAsync(new AppRole { Name = Roles.DemoUser }), "create the demo role");
            role = await roleManager.FindByNameAsync(Roles.DemoUser)
                ?? throw new InvalidOperationException("Demo role could not be loaded after creation.");
        }

        var claims = (await roleManager.GetClaimsAsync(role)).Where(claim => claim.Type == Permissions.ClaimType).ToList();
        foreach (var extra in claims.Where(claim => !Roles.DemoUserPermissions.Contains(claim.Value)))
            EnsureSucceeded(await roleManager.RemoveClaimAsync(role, extra), $"remove '{extra.Value}' from the demo role");
        foreach (var permission in Roles.DemoUserPermissions.Where(permission => claims.All(claim => claim.Value != permission)))
            EnsureSucceeded(await roleManager.AddClaimAsync(role, new Claim(Permissions.ClaimType, permission)),
                $"add '{permission}' to the demo role");

        var user = await userManager.FindByNameAsync(DemoModeOptions.DemoUserName);
        if (user is null)
        {
            user = new AppUser
            {
                UserName = DemoModeOptions.DemoUserName,
                Email = DemoModeOptions.DemoUserEmail,
                EmailConfirmed = true
            };
            EnsureSucceeded(await userManager.CreateAsync(user), "create the demo user");
        }

        // Never let the shared account keep elevated access or a password, even if someone changed it by hand.
        if (await userManager.HasPasswordAsync(user))
            EnsureSucceeded(await userManager.RemovePasswordAsync(user), "remove the demo user's password");
        foreach (var other in (await userManager.GetRolesAsync(user)).Where(name => name != Roles.DemoUser))
            EnsureSucceeded(await userManager.RemoveFromRoleAsync(user, other), $"remove role '{other}' from the demo user");
        foreach (var claim in (await userManager.GetClaimsAsync(user)).Where(claim => claim.Type == Permissions.ClaimType))
            EnsureSucceeded(await userManager.RemoveClaimAsync(user, claim), "remove a direct permission from the demo user");
        if (!await userManager.IsInRoleAsync(user, Roles.DemoUser))
            EnsureSucceeded(await userManager.AddToRoleAsync(user, Roles.DemoUser), "assign the demo role");
    }

    private static void EnsureSucceeded(IdentityResult result, string operation)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(
                $"Unable to {operation}: {string.Join("; ", result.Errors.Select(error => error.Description))}");
    }
}
