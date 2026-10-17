using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Infrastructure.Identity.Jwt;
using SupplyGuard.Infrastructure.Identity.Seeding;
using SupplyGuard.Infrastructure.Identity.Services;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Interceptors;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using SupplyGuard.Infrastructure.Persistence.Repositories;

namespace SupplyGuard.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddOutboxProcessing(this IServiceCollection services, IConfiguration configuration)
    {
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(IMessagePublisher)))
        {
            throw new InvalidOperationException(
                "Register a broker-confirming IMessagePublisher before enabling outbox processing.");
        }

        services.AddOptions<OutboxProcessorOptions>()
            .Bind(configuration.GetSection(OutboxProcessorOptions.SectionName))
            .Validate(settings => settings.BatchSize is >= 1 and <= 1_000, "Outbox batch size must be between 1 and 1000.")
            .Validate(settings => settings.PollInterval > TimeSpan.Zero && settings.PollInterval <= TimeSpan.FromHours(1),
                "Outbox polling interval must be positive and at most one hour.")
            .Validate(settings => settings.PublishTimeout > TimeSpan.Zero && settings.PublishTimeout <= TimeSpan.FromMinutes(5),
                "Outbox publication timeout must be positive and at most five minutes.")
            .Validate(settings => settings.RetryDelay > TimeSpan.Zero && settings.MaxRetryDelay >= settings.RetryDelay &&
                                  settings.MaxRetryDelay <= TimeSpan.FromDays(1),
                "Outbox retry delay must be positive, with a maximum delay between the initial delay and one day.")
            .ValidateOnStart();

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<OutboxDispatcher>();
        services.AddHostedService<OutboxProcessor>();
        return services;
    }

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
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddScoped<IdentitySeeder>();

        return services;
    }
}
