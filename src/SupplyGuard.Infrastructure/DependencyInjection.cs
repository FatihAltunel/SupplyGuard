using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Infrastructure.Identity.Jwt;
using SupplyGuard.Infrastructure.Identity.Seeding;
using SupplyGuard.Infrastructure.Identity.Services;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Interceptors;
using SupplyGuard.Infrastructure.Persistence.Repositories;

namespace SupplyGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("SupplyGuardDatabase")
            ?? throw new InvalidOperationException("Connection string 'SupplyGuardDatabase' was not found.");

        if (connectionString.Contains("<YOUR_", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Connection string 'SupplyGuardDatabase' contains a public placeholder. " +
                "Set it with User Secrets or the ConnectionStrings__SupplyGuardDatabase environment variable.");
        }

        services.AddScoped<AuditingSaveChangesInterceptor>();
        services.AddDbContext<SupplyGuardDbContext>((serviceProvider, options) =>
        {
            options.UseNpgsql(connectionString);
            options.AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>());
        });

        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<ITokenService, JwtTokenService>();
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IRiskManagementRepository, RiskManagementRepository>();
        services.AddScoped<IdentitySeeder>();

        return services;
    }
}
