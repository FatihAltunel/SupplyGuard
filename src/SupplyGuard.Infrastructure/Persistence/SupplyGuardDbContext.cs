using Microsoft.EntityFrameworkCore;
using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence;

public sealed class SupplyGuardDbContext(DbContextOptions<SupplyGuardDbContext> options) : DbContext(options)
{
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<RiskAssessment> RiskAssessments => Set<RiskAssessment>();
    public DbSet<RiskScore> RiskScores => Set<RiskScore>();
    public DbSet<RiskIndicator> RiskIndicators => Set<RiskIndicator>();
    public DbSet<EarlyWarning> EarlyWarnings => Set<EarlyWarning>();
    public DbSet<XAIAuditLog> XAIAuditLogs => Set<XAIAuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("supplyguard");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(SupplyGuardDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
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
}
