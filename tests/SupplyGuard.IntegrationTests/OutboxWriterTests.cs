using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.Events;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Events;
using SupplyGuard.Domain.Services;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Interceptors;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using Xunit;

namespace SupplyGuard.IntegrationTests;

// These checks use the real Npgsql model and DI registrations without connecting to a database.
public class OutboxWriterTests
{
    private const string UnusedConnectionString =
        "Host=127.0.0.1;Port=1;Database=outbox_tests;Username=unused;Password=unused";

    [Fact]
    public async Task WriteAsync_StagesCompleteConcreteContractWithoutSaving()
    {
        var interceptor = new StopBeforeDatabaseInterceptor();
        var options = new DbContextOptionsBuilder<SupplyGuardDbContext>()
            .UseNpgsql(UnusedConnectionString)
            .AddInterceptors(interceptor)
            .Options;
        await using var context = new SupplyGuardDbContext(options);
        IOutboxWriter writer = new OutboxWriter(context);
        var message = new EarlyWarningRaisedV1(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, "Critical", "outbox-test",
            new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero));

        await writer.WriteAsync(message);

        var entry = Assert.Single(context.ChangeTracker.Entries<OutboxMessage>());
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal(message.MessageId, entry.Entity.Id);
        Assert.Equal(message.MessageType, entry.Entity.MessageType);
        Assert.Equal(message.CorrelationId, entry.Entity.CorrelationId);
        Assert.Equal(message, JsonSerializer.Deserialize<EarlyWarningRaisedV1>(entry.Entity.Payload));
        Assert.Equal(0, interceptor.SaveCalls);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task RegisteredWriterAndRepository_StageBusinessAndOutboxChangesInTheSameSave()
    {
        var interceptor = new StopBeforeDatabaseInterceptor();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SupplyGuardDatabase"] = UnusedConnectionString
        }).Build();
        var services = new ServiceCollection();
        services.AddScoped<ICurrentUserService, NoCurrentUser>();
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddScoped<DbContextOptions<SupplyGuardDbContext>>(serviceProvider =>
            new DbContextOptionsBuilder<SupplyGuardDbContext>()
                .UseNpgsql(UnusedConnectionString)
                .AddInterceptors(serviceProvider.GetRequiredService<AuditingSaveChangesInterceptor>(), interceptor)
                .Options);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SupplyGuardDbContext>();
        var supplier = new Supplier("Outbox Supplier", Guid.NewGuid().ToString("N"), "TR");
        var occurredAtUtc = new DateTimeOffset(2026, 10, 16, 9, 0, 0, TimeSpan.Zero);
        supplier.AddRiskIndicator(new RiskIndicator(
            supplier.Id, RiskCategory.Financial, "FIN-01", "Exposure", RiskLevel.Critical,
            90m, 90m, 1m, "Test", occurredAtUtc));
        var assessment = new RiskAssessmentService().Evaluate(
            supplier, supplier.RiskIndicators.ToArray(), occurredAtUtc);
        supplier.ApplyRiskAssessment(assessment, "shared-unit-of-work");
        context.Suppliers.Add(supplier);
        var warningEvent = Assert.Single(supplier.DomainEvents.OfType<EarlyWarningTriggeredEvent>());
        var handler = scope.ServiceProvider.GetRequiredService<IDomainEventHandler<EarlyWarningTriggeredEvent>>();
        var repository = scope.ServiceProvider.GetRequiredService<IRiskManagementRepository>();

        await handler.HandleAsync(warningEvent);
        Assert.Equal(0, interceptor.SaveCalls);

        // Stop at the save boundary: no server or persisted data is needed for this wiring check.
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => repository.SaveChangesAsync());
        Assert.Equal("Test stopped the save before any database access.", failure.Message);

        Assert.Equal(1, interceptor.SaveCalls);
        Assert.Contains(interceptor.PendingEntities, entity => ReferenceEquals(entity, supplier));
        Assert.Contains(interceptor.PendingEntities, entity => entity is EarlyWarning warning && warning.Id == warningEvent.EarlyWarningId);
        Assert.Contains(interceptor.PendingEntities, entity => entity is RiskAssessment risk && risk.Id == warningEvent.RiskAssessmentId);
        var outbox = Assert.Single(interceptor.PendingEntities.OfType<OutboxMessage>());
        Assert.Equal(warningEvent.EarlyWarningId, JsonSerializer.Deserialize<EarlyWarningRaisedV1>(outbox.Payload)!.WarningId);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class StopBeforeDatabaseInterceptor : SaveChangesInterceptor
    {
        public int SaveCalls { get; private set; }
        public object[] PendingEntities { get; private set; } = [];

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            CaptureAndStop(eventData.Context!);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            CaptureAndStop(eventData.Context!);
            return ValueTask.FromResult(result);
        }

        private void CaptureAndStop(DbContext context)
        {
            SaveCalls++;
            PendingEntities = context.ChangeTracker.Entries()
                .Where(entry => entry.State == EntityState.Added)
                .Select(entry => entry.Entity)
                .ToArray();
            throw new InvalidOperationException("Test stopped the save before any database access.");
        }
    }

    private sealed class NoCurrentUser : ICurrentUserService
    {
        public Guid? UserId => null;
        public bool IsAuthenticated => false;
        public string? UserName => null;
        public IReadOnlyCollection<string> Roles => [];
        public bool HasPermission(string permission) => false;
    }
}
