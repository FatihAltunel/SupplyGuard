using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Models;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure.Identity.Entities;

namespace SupplyGuard.Infrastructure.Identity.Services;

public sealed class IdentityService(
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager) : IIdentityService
{
    public async Task<IdentityOperationResult> CreateUserAsync(
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new AppUser { UserName = userName, Email = email };
        var result = await userManager.CreateAsync(user, password);
        return ToOperationResult(result);
    }

    public async Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string userNameOrEmail,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByNameAsync(userNameOrEmail)
            ?? await userManager.FindByEmailAsync(userNameOrEmail);

        if (user is null || !await userManager.CheckPasswordAsync(user, password))
        {
            return null;
        }

        return await CreateAuthenticatedUserAsync(user);
    }

    public async Task<AuthenticatedUser?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : await CreateAuthenticatedUserAsync(user);
    }

    public async Task<IdentityOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = await userManager.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return IdentityOperationResult.Failure(["User was not found."]);
        }

        if (!await roleManager.RoleExistsAsync(role))
        {
            return IdentityOperationResult.Failure(["Role was not found."]);
        }

        return ToOperationResult(await userManager.AddToRoleAsync(user, role));
    }

    private async Task<AuthenticatedUser> CreateAuthenticatedUserAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var claims = await userManager.GetClaimsAsync(user);
        var permissions = claims
            .Where(claim => claim.Type == Permissions.ClaimType)
            .Select(claim => claim.Value)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var roleName in roles)
        {
            var role = await roleManager.FindByNameAsync(roleName);
            if (role is null)
            {
                continue;
            }

            var roleClaims = await roleManager.GetClaimsAsync(role);
            foreach (var claim in roleClaims.Where(claim => claim.Type == Permissions.ClaimType))
            {
                permissions.Add(claim.Value);
            }
        }

        return new AuthenticatedUser(
            user.Id,
            user.UserName ?? throw new InvalidOperationException("Identity user name is required."),
            user.Email,
            roles.ToArray(),
            permissions.ToArray());
    }

    private static IdentityOperationResult ToOperationResult(IdentityResult result) =>
        result.Succeeded
            ? IdentityOperationResult.Success()
            : IdentityOperationResult.Failure(result.Errors.Select(error => error.Description));
}
