using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Models;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure.Identity.Entities;
using SupplyGuard.Infrastructure.Identity.Jwt;
using SupplyGuard.Infrastructure.Persistence;

namespace SupplyGuard.Infrastructure.Identity.Services;

public sealed class JwtTokenService(
    SupplyGuardDbContext dbContext,
    UserManager<AppUser> userManager,
    RoleManager<AppRole> roleManager,
    IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public async Task<TokenResult> CreateTokenPairAsync(
        AuthenticatedUser user,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions();
        var utcNow = DateTimeOffset.UtcNow;
        var rawRefreshToken = CreateRefreshTokenValue();
        var refreshToken = new RefreshToken(
            user.Id,
            ComputeHash(rawRefreshToken),
            utcNow.AddDays(_options.RefreshTokenLifetimeDays));

        dbContext.RefreshTokens.Add(refreshToken);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreateTokenResult(user, rawRefreshToken, refreshToken.ExpiresAtUtc, utcNow);
    }

    public async Task<TokenResult?> RefreshTokenPairAsync(
        string refreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return null;
        }

        ValidateOptions();
        var utcNow = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        var existingToken = await dbContext.RefreshTokens
            .Include(token => token.User)
            .SingleOrDefaultAsync(token => token.TokenHash == ComputeHash(refreshToken), cancellationToken);

        if (existingToken is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return null;
        }

        if (!existingToken.IsActiveAt(utcNow))
        {
            if (existingToken.RevokedAtUtc is not null)
            {
                await RevokeActiveTokensForUserAsync(existingToken.UserId, utcNow, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var newRefreshTokenValue = CreateRefreshTokenValue();
        var replacement = new RefreshToken(
            existingToken.UserId,
            ComputeHash(newRefreshTokenValue),
            utcNow.AddDays(_options.RefreshTokenLifetimeDays));
        existingToken.Revoke(utcNow, replacement.Id);
        dbContext.RefreshTokens.Add(replacement);

        var user = await CreateAuthenticatedUserAsync(existingToken.User);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return CreateTokenResult(user, newRefreshTokenValue, replacement.ExpiresAtUtc, utcNow);
    }

    public async Task RevokeRefreshTokenAsync(string refreshToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return;
        }

        var existingToken = await dbContext.RefreshTokens
            .SingleOrDefaultAsync(token => token.TokenHash == ComputeHash(refreshToken), cancellationToken);

        if (existingToken is null || !existingToken.IsActiveAt(DateTimeOffset.UtcNow))
        {
            return;
        }

        existingToken.Revoke(DateTimeOffset.UtcNow);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private TokenResult CreateTokenResult(
        AuthenticatedUser user,
        string refreshToken,
        DateTimeOffset refreshTokenExpiresAtUtc,
        DateTimeOffset utcNow)
    {
        var accessTokenExpiresAtUtc = utcNow.AddMinutes(_options.AccessTokenLifetimeMinutes);
        var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(signingKey, SecurityAlgorithms.HmacSha256);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.UserName),
            new(JwtRegisteredClaimNames.UniqueName, user.UserName),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(user.Email))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Email, user.Email));
        }

        claims.AddRange(user.Roles.Select(role => new Claim(ClaimTypes.Role, role)));
        claims.AddRange(user.Permissions.Select(permission => new Claim(Permissions.ClaimType, permission)));

        var jwt = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: utcNow.UtcDateTime,
            expires: accessTokenExpiresAtUtc.UtcDateTime,
            signingCredentials: credentials);

        return new TokenResult(
            new JwtSecurityTokenHandler().WriteToken(jwt),
            accessTokenExpiresAtUtc,
            refreshToken,
            refreshTokenExpiresAtUtc);
    }

    private async Task RevokeActiveTokensForUserAsync(Guid userId, DateTimeOffset utcNow, CancellationToken cancellationToken)
    {
        var activeTokens = await dbContext.RefreshTokens
            .Where(token => token.UserId == userId && token.RevokedAtUtc == null && token.ExpiresAtUtc > utcNow)
            .ToListAsync(cancellationToken);

        foreach (var token in activeTokens)
        {
            token.Revoke(utcNow);
        }
    }

    private async Task<AuthenticatedUser> CreateAuthenticatedUserAsync(AppUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        var permissions = (await userManager.GetClaimsAsync(user))
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

            foreach (var claim in await roleManager.GetClaimsAsync(role))
            {
                if (claim.Type == Permissions.ClaimType)
                {
                    permissions.Add(claim.Value);
                }
            }
        }

        return new AuthenticatedUser(
            user.Id,
            user.UserName ?? throw new InvalidOperationException("Identity user name is required."),
            user.Email,
            roles.ToArray(),
            permissions.ToArray());
    }

    private static string CreateRefreshTokenValue() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

    private static string ComputeHash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private void ValidateOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.Issuer) ||
            string.IsNullOrWhiteSpace(_options.Audience) ||
            (_options.SigningKey?.Contains("<YOUR_", StringComparison.OrdinalIgnoreCase) ?? true) ||
            Encoding.UTF8.GetByteCount(_options.SigningKey ?? string.Empty) < 32 ||
            _options.AccessTokenLifetimeMinutes <= 0 ||
            _options.RefreshTokenLifetimeDays <= 0)
        {
            throw new InvalidOperationException("JWT configuration is invalid.");
        }
    }
}
