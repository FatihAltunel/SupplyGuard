using Microsoft.AspNetCore.Authorization;
using SupplyGuard.Application.Security;

namespace SupplyGuard.WebAPI.Authentication.Authorization;

public static class AuthorizationPolicies
{
    public static void Configure(AuthorizationOptions options)
    {
        options.FallbackPolicy = new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .Build();

        foreach (var permission in Permissions.All)
        {
            options.AddPolicy(permission, policy =>
                policy.RequireClaim(Permissions.ClaimType, permission));
        }
    }
}
