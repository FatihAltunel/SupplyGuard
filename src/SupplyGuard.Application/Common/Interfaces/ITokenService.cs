using SupplyGuard.Application.Common.Models;

namespace SupplyGuard.Application.Common.Interfaces;

public interface ITokenService
{
    Task<TokenResult> CreateTokenPairAsync(
        AuthenticatedUser user,
        CancellationToken cancellationToken = default);

    Task<TokenResult?> RefreshTokenPairAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);

    Task RevokeRefreshTokenAsync(
        string refreshToken,
        CancellationToken cancellationToken = default);
}
