using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Security;

namespace SupplyGuard.WebAPI.Authentication;

public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private ClaimsPrincipal? Principal => httpContextAccessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirstValue(ClaimTypes.NameIdentifier)
                ?? Principal?.FindFirstValue(JwtRegisteredClaimNames.Sub);
            return Guid.TryParse(value, out var userId) ? userId : null;
        }
    }

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated == true;
    public string? UserName => Principal?.Identity?.Name;
    public IReadOnlyCollection<string> Roles =>
        Principal?.FindAll(ClaimTypes.Role).Select(claim => claim.Value).ToArray()
        ?? Array.Empty<string>();

    public bool HasPermission(string permission) =>
        Principal?.HasClaim(Permissions.ClaimType, permission) == true;
}
