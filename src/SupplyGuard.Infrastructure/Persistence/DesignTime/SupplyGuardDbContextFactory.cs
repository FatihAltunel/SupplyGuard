using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupplyGuard.Infrastructure.Persistence.DesignTime;

public sealed class SupplyGuardDbContextFactory : IDesignTimeDbContextFactory<SupplyGuardDbContext>
{
    public SupplyGuardDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPPLYGUARD_CONNECTION_STRING")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__SupplyGuardDatabase")
            ?? throw new InvalidOperationException(
                "Set SUPPLYGUARD_CONNECTION_STRING or ConnectionStrings__SupplyGuardDatabase before running " +
                "EF Core design-time commands.");

        var optionsBuilder = new DbContextOptionsBuilder<SupplyGuardDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SupplyGuardDbContext(optionsBuilder.Options);
    }
}
