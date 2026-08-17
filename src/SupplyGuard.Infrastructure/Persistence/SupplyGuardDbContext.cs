using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Infrastructure.Identity.Entities;

namespace SupplyGuard.Infrastructure.Persistence;

public sealed class SupplyGuardDbContext(DbContextOptions<SupplyGuardDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public const string DomainSchema = "supplyguard";
    public const string IdentitySchema = "identity";

    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();
    public DbSet<RiskScore> RiskScores => Set<RiskScore>();
    public DbSet<RiskIndicator> RiskIndicators => Set<RiskIndicator>();
    public DbSet<EarlyWarning> EarlyWarnings => Set<EarlyWarning>();
    public DbSet<XAIAuditLog> XAIAuditLogs => Set<XAIAuditLog>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(DomainSchema);
        ConfigureIdentityTables(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupplyGuardDbContext).Assembly);
    }

    public override int SaveChanges()
    {
        ApplySoftDeletes();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        ApplySoftDeletes();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void ApplySoftDeletes()
    {
        foreach (var entry in ChangeTracker.Entries<ISoftDeletable>()
                     .Where(entry => entry.State == EntityState.Deleted))
        {
            entry.Entity.MarkAsDeleted();
            entry.State = EntityState.Modified;
        }
    }

    private static void ConfigureIdentityTables(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AppUser>().ToTable("users", IdentitySchema);
        modelBuilder.Entity<AppRole>().ToTable("roles", IdentitySchema);
        modelBuilder.Entity<IdentityUserRole<Guid>>().ToTable("user_roles", IdentitySchema);
        modelBuilder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims", IdentitySchema);
        modelBuilder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins", IdentitySchema);
        modelBuilder.Entity<IdentityRoleClaim<Guid>>().ToTable("role_claims", IdentitySchema);
        modelBuilder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens", IdentitySchema);
    }
}
