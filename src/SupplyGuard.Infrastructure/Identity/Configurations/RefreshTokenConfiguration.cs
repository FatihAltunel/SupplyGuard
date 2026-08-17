using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupplyGuard.Infrastructure.Identity.Entities;
using SupplyGuard.Infrastructure.Persistence;

namespace SupplyGuard.Infrastructure.Identity.Configurations;

public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens", SupplyGuardDbContext.IdentitySchema);
        builder.HasKey(token => token.Id);

        builder.Property(token => token.Id).HasColumnType("uuid").ValueGeneratedNever();
        builder.Property(token => token.UserId).HasColumnType("uuid").IsRequired();
        builder.Property(token => token.TokenHash).HasMaxLength(128).IsRequired();
        builder.Property(token => token.CreatedAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.ExpiresAtUtc).HasColumnType("timestamp with time zone").IsRequired();
        builder.Property(token => token.RevokedAtUtc).HasColumnType("timestamp with time zone");
        builder.Property(token => token.ReplacedByTokenId).HasColumnType("uuid");
        builder.Property(token => token.DeviceName).HasMaxLength(256);
        builder.Property(token => token.IpAddress).HasMaxLength(64);

        builder.HasIndex(token => token.TokenHash).IsUnique();
        builder.HasIndex(token => new { token.UserId, token.ExpiresAtUtc });
        builder.HasOne(token => token.User)
            .WithMany(user => user.RefreshTokens)
            .HasForeignKey(token => token.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
