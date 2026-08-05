using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SupplyGuard.Infrastructure.Persistence.DesignTime;

public sealed class SupplyGuardDbContextFactory : IDesignTimeDbContextFactory<SupplyGuardDbContext>
{
    private const string DefaultConnectionString =
        "Host=localhost;Port=5432;Database=supplyguard_db;Username=supplyguard;Password=supplyguard_dev_password;";

    public SupplyGuardDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("SUPPLYGUARD_CONNECTION_STRING")
            ?? DefaultConnectionString;

        var optionsBuilder = new DbContextOptionsBuilder<SupplyGuardDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new SupplyGuardDbContext(optionsBuilder.Options);
    }
}
