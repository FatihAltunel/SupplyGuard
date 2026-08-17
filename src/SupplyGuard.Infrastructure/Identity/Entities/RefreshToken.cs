namespace SupplyGuard.Infrastructure.Identity.Entities;

public sealed class RefreshToken
{
    private RefreshToken()
    {
        // Required by EF Core.
    }

    public RefreshToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAtUtc,
        string? deviceName = null,
        string? ipAddress = null)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User ID cannot be empty.", nameof(userId));
        }

        if (string.IsNullOrWhiteSpace(tokenHash))
        {
            throw new ArgumentException("Token hash is required.", nameof(tokenHash));
        }

        if (expiresAtUtc <= DateTimeOffset.UtcNow)
        {
            throw new ArgumentOutOfRangeException(nameof(expiresAtUtc), "Refresh token expiry must be in the future.");
        }

        Id = Guid.NewGuid();
        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = DateTimeOffset.UtcNow;
        ExpiresAtUtc = expiresAtUtc.ToUniversalTime();
        DeviceName = NormalizeOptional(deviceName, 256);
        IpAddress = NormalizeOptional(ipAddress, 64);
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public DateTimeOffset ExpiresAtUtc { get; private set; }
    public DateTimeOffset? RevokedAtUtc { get; private set; }
    public Guid? ReplacedByTokenId { get; private set; }
    public string? DeviceName { get; private set; }
    public string? IpAddress { get; private set; }

    public AppUser User { get; private set; } = null!;

    public bool IsActiveAt(DateTimeOffset utcNow) =>
        RevokedAtUtc is null && ExpiresAtUtc > utcNow.ToUniversalTime();

    public void Revoke(DateTimeOffset revokedAtUtc, Guid? replacedByTokenId = null)
    {
        if (RevokedAtUtc is not null)
        {
            return;
        }

        if (replacedByTokenId == Guid.Empty)
        {
            throw new ArgumentException("Replacement token ID cannot be empty.", nameof(replacedByTokenId));
        }

        RevokedAtUtc = revokedAtUtc.ToUniversalTime();
        ReplacedByTokenId = replacedByTokenId;
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length > maximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maximumLength} characters.");
        }

        return normalized;
    }
}
