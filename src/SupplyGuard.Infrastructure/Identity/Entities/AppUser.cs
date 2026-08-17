using Microsoft.AspNetCore.Identity;

namespace SupplyGuard.Infrastructure.Identity.Entities;

public sealed class AppUser : IdentityUser<Guid>
{
    private readonly List<RefreshToken> _refreshTokens = [];

    public IReadOnlyCollection<RefreshToken> RefreshTokens => _refreshTokens.AsReadOnly();
}
