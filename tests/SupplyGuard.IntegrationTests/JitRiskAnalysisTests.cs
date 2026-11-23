using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Integrations;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Configurations;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using SupplyGuard.Infrastructure.Persistence.Repositories;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class JitRiskAnalysisTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CountActive_FiltersSupplierInactiveAndSoftDeletedIndicators()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Context();
        var supplier = await SeedAsync(context);
        context.ChangeTracker.Clear();

        Assert.Equal(3, await new SupplierRiskIndicatorReader(context).CountActiveAsync(supplier.Id));
        Assert.Equal(0, await new SupplierRiskIndicatorReader(context).CountActiveAsync(Guid.NewGuid()));
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Handler_UsesPureDiAndPersistsExactlyTheWorkerBody_WithSeparateDeliveryMetadata()
    {
        await using var store = await Store.CreateAsync();
        Guid supplierId;
        await using (var seed = store.Context()) supplierId = (await SeedAsync(seed)).Id;

        var services = new ServiceCollection();
        services.AddJitRiskAnalysis();
        services.AddSimulatedSupplierIntegrations();
        services.AddScoped(_ => store.Context());
        services.AddScoped<ISupplierRepository, SupplierRepository>();
        services.AddScoped<IXAIAuditLogRepository, XAIAuditLogRepository>();
        services.AddScoped<IRiskAnalysisRepository, RiskManagementRepository>();
        services.AddScoped<ISupplierRiskIndicatorReader, SupplierRiskIndicatorReader>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOutboxWriter, OutboxWriter>();
        services.AddSingleton<IIntegrationMessageSerializer, IntegrationMessageSerializer>();
        services.AddSingleton<IRiskAnalysisResponseSerializer, RiskAnalysisResponseSerializer>();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true, ValidateOnBuild = true
        });
        await using var scope = provider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestJitRiskAnalysisCommand, RequestJitRiskAnalysisResult>>();
        var accepted = await handler.HandleAsync(Command(supplierId));
        Assert.True(accepted.IsSuccess);

        await using var verification = store.Context();
        var row = await verification.OutboxMessages.SingleAsync();
        var audit = await verification.XAIAuditLogs.SingleAsync();
        Assert.Equal(ExplanationStatus.Pending, audit.ExplanationStatus);
        Assert.Equal(row.Id, audit.RequestMessageId);
        Assert.Equal(row.Payload, audit.RequestPayload);
        Assert.Equal(Now, audit.RequestedAtUtc);
        Assert.Equal(accepted.Value!.RequestMessageId, row.Id);
        Assert.Equal("req-998877", row.CorrelationId);
        Assert.Equal(RiskAnalysisRequest.TypeName, row.MessageType);
        Assert.Equal(Now, row.OccurredAtUtc);
        Assert.Null(row.ProcessedAtUtc);
        using var document = JsonDocument.Parse(row.Payload);
        var body = document.RootElement;
        Assert.Equal(new[] { "CorrelationId", "SupplierId", "averageDeliveryDelayDays", "onTimeDeliveryRate", "qualityDefectRate", "DeliveryDelayTrend_Percentage", "FinancialStabilityScore", "ActiveRiskIndicatorsCount" }, body.EnumerateObject().Select(property => property.Name));
        Assert.Equal(supplierId, body.GetProperty("SupplierId").GetGuid());
        Assert.Equal("req-998877", body.GetProperty("CorrelationId").GetString());
        var features = body;
        Assert.Equal(8, features.EnumerateObject().Count());
        Assert.Equal(1.5m, features.GetProperty("averageDeliveryDelayDays").GetDecimal());
        Assert.Equal(0.95m, features.GetProperty("onTimeDeliveryRate").GetDecimal());
        Assert.Equal(0.015m, features.GetProperty("qualityDefectRate").GetDecimal());
        Assert.Equal(15m, features.GetProperty("DeliveryDelayTrend_Percentage").GetDecimal());
        Assert.Equal(85m, features.GetProperty("FinancialStabilityScore").GetDecimal());
        Assert.Equal(3, features.GetProperty("ActiveRiskIndicatorsCount").GetInt32());

        // A failed publication must retain the original body and AMQP identifiers.
        var published = new List<RiskAnalysisRequest>();
        var publisher = new Mock<IMessagePublisher>();
        publisher.Setup(value => value.PublishAsync(It.IsAny<IIntegrationMessage>(), It.IsAny<CancellationToken>()))
            .Returns<IIntegrationMessage, CancellationToken>((message, _) =>
            {
                published.Add(Assert.IsType<RiskAnalysisRequest>(message));
                return published.Count == 1 ? Task.FromException(new IOException("Broker unavailable")) : Task.CompletedTask;
            });
        var dispatchServices = new ServiceCollection();
        dispatchServices.AddScoped(_ => store.Context());
        dispatchServices.AddSingleton(publisher.Object);
        dispatchServices.AddSingleton<IIntegrationMessageSerializer, IntegrationMessageSerializer>();
        await using var dispatchProvider = dispatchServices.BuildServiceProvider();
        var clock = new FixedTimeProvider(Now);
        var dispatcher = new OutboxDispatcher(dispatchProvider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxProcessorOptions()), clock, NullLogger<OutboxDispatcher>.Instance);
        Assert.False(await dispatcher.TryDispatchAsync(row.Id));
        clock.Now = Now.AddHours(1);
        Assert.True(await dispatcher.TryDispatchAsync(row.Id));
        Assert.Equal(2, published.Count);
        var serializer = new IntegrationMessageSerializer();
        Assert.All(published, message =>
        {
            Assert.Equal(row.Id, message.MessageId);
            Assert.Equal(row.OccurredAtUtc, message.OccurredAtUtc);
            Assert.Equal(row.CorrelationId, message.CorrelationId);
            Assert.Equal(row.Payload, serializer.Serialize(message));
        });
        await using var dispatched = store.Context();
        var delivered = await dispatched.OutboxMessages.SingleAsync();
        Assert.Equal(1, delivered.RetryCount);
        Assert.NotNull(delivered.ProcessedAtUtc);
        Assert.Equal(row.Payload, delivered.Payload);
    }

    [Theory]
    [InlineData("missing-supplier")]
    [InlineData("missing-trend")]
    [InlineData("invalid-stability")]
    [InlineData("service-failure")]
    [InlineData("cancelled")]
    public async Task IncompleteOrInvalidData_DoesNotStageAnOutboxMessage(string scenario)
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Context();
        var supplier = await SeedAsync(context);
        var performance = new Mock<ISupplierPerformanceService>();
        performance.Setup(value => value.GetDeliveryDelayTrendAsync(supplier.Id, TimeSpan.FromDays(30), It.IsAny<CancellationToken>()))
            .ReturnsAsync(scenario == "missing-trend" ? (decimal?)null : 15m);
        var financial = new Mock<IFinancialIntegrationService>();
        financial.Setup(value => value.GetStabilityScoreAsync(supplier.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(scenario == "invalid-stability" ? 101m : 85m);
        if (scenario == "service-failure")
            financial.Setup(value => value.GetStabilityScoreAsync(supplier.Id, It.IsAny<CancellationToken>()))
                .ThrowsAsync(new IOException("ERP unavailable"));
        if (scenario == "cancelled")
            performance.Setup(value => value.GetDeliveryDelayTrendAsync(supplier.Id, TimeSpan.FromDays(30), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new OperationCanceledException());
        var unit = new Mock<IUnitOfWork>(MockBehavior.Strict);
        var writer = new Mock<IOutboxWriter>(MockBehavior.Strict);
        var handler = new RequestJitRiskAnalysisCommandHandler(new SupplierRepository(context),
            new SupplierRiskIndicatorReader(context), performance.Object, financial.Object,
            new SupplierRiskFeaturesValidator(), writer.Object, unit.Object, new FixedTimeProvider(Now),
            new XAIAuditLogRepository(context), new RiskManagementRepository(context), new IntegrationMessageSerializer());
        var command = Command(scenario == "missing-supplier" ? Guid.NewGuid() : supplier.Id);
        if (scenario == "service-failure")
            await Assert.ThrowsAsync<IOException>(() => handler.HandleAsync(command));
        else if (scenario == "cancelled")
            await Assert.ThrowsAsync<OperationCanceledException>(() => handler.HandleAsync(command));
        else
        {
            var result = await handler.HandleAsync(command);
            Assert.False(result.IsSuccess);
            var code = scenario switch
            {
                "missing-supplier" => "Supplier.NotFound",
                "missing-trend" => "RiskAnalysis.TrendUnavailable",
                _ => "RiskAnalysis.InvalidFeatures"
            };
            Assert.Equal(code, Assert.Single(result.Errors).Code);
        }
        unit.VerifyNoOtherCalls();
        writer.VerifyNoOtherCalls();
        Assert.Empty(await context.OutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task FailedCommit_RollsBackAndDiscardsStagedSnapshot()
    {
        await using var store = await Store.CreateAsync();
        await using var context = store.Context();
        var supplier = await SeedAsync(context);
        await using var realUnit = new EfUnitOfWork(context);
        var unit = new Mock<IUnitOfWork>();
        unit.Setup(value => value.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(realUnit.BeginTransactionAsync);
        unit.Setup(value => value.CommitAsync(It.IsAny<CancellationToken>())).ThrowsAsync(new DbUpdateException("Database failure"));
        unit.Setup(value => value.RollbackAsync(CancellationToken.None)).Returns(() => realUnit.RollbackAsync());
        var handler = new RequestJitRiskAnalysisCommandHandler(new SupplierRepository(context),
            new SupplierRiskIndicatorReader(context), new SimulatedSupplierPerformanceService(),
            new SimulatedFinancialIntegrationService(), new SupplierRiskFeaturesValidator(),
            new OutboxWriter(context, new IntegrationMessageSerializer()), unit.Object, new FixedTimeProvider(Now),
            new XAIAuditLogRepository(context), new RiskManagementRepository(context), new IntegrationMessageSerializer());

        await Assert.ThrowsAsync<DbUpdateException>(() => handler.HandleAsync(Command(supplier.Id)));
        unit.Verify(value => value.RollbackAsync(CancellationToken.None), Times.Once);
        Assert.Empty(context.ChangeTracker.Entries());
        Assert.Empty(await context.OutboxMessages.ToListAsync());
        Assert.Empty(await context.XAIAuditLogs.ToListAsync());
    }

    [Theory]
    [InlineData(1, 1.15, 15)]
    [InlineData(2, 1, -50)]
    [InlineData(2, 2, 0)]
    [InlineData(2, 0, -100)]
    public void Trend_UsesRelativeChangeAndPreservesDirection(double previous, double current, double expected) =>
        Assert.Equal((decimal)expected, SimulatedSupplierPerformanceService.CalculateTrend([(decimal)previous], [(decimal)current]));

    [Fact]
    public void Trend_MissingSamplesOrZeroBaselineAreUnavailable()
    {
        Assert.Null(SimulatedSupplierPerformanceService.CalculateTrend([], [1m]));
        Assert.Null(SimulatedSupplierPerformanceService.CalculateTrend([1m], []));
        Assert.Null(SimulatedSupplierPerformanceService.CalculateTrend([0m], [1m]));
        Assert.Null(SimulatedSupplierPerformanceService.CalculateTrend([0m], [0m]));
    }

    [Theory]
    [InlineData("wrong-case")]
    [InlineData("quoted-number")]
    [InlineData("missing")]
    [InlineData("duplicate")]
    [InlineData("unknown")]
    [InlineData("invalid-range")]
    public void WorkerContract_RejectsMalformedPayloads(string scenario)
    {
        var serializer = new IntegrationMessageSerializer();
        var message = new RiskAnalysisRequest("req-998877", Guid.NewGuid(), 1.5m, .95m, .015m, 15m, 85m, 3)
        {
            MessageId = Guid.NewGuid(), OccurredAtUtc = Now
        };
        var json = serializer.Serialize(message);
        var node = JsonNode.Parse(json)!;
        var features = node.AsObject();
        switch (scenario)
        {
            case "wrong-case": node["supplierId"] = node["SupplierId"]!.DeepClone(); node.AsObject().Remove("SupplierId"); break;
            case "quoted-number": features["onTimeDeliveryRate"] = "0.95"; break;
            case "missing": features.Remove("ActiveRiskIndicatorsCount"); break;
            case "unknown": features["Unexpected"] = 1; break;
            case "invalid-range": features["onTimeDeliveryRate"] = 95; break;
            case "duplicate":
                json = json.Replace("\"ActiveRiskIndicatorsCount\":3", "\"ActiveRiskIndicatorsCount\":3,\"ActiveRiskIndicatorsCount\":3");
                break;
        }
        if (scenario != "duplicate") json = node.ToJsonString();
        Assert.Throws<JsonException>(() => serializer.Deserialize<RiskAnalysisRequest>(json));
    }

    private static RequestJitRiskAnalysisCommand Command(Guid supplierId) => new(supplierId, "req-998877", 1.5m, .95m, .015m);

    [Fact]
    public async Task RequestReplay_ReusesOriginalOutboxAndAudit_AndRejectsChangedFeatures()
    {
        await using var store = await Store.CreateAsync();
        Guid supplierId;
        await using (var seed = store.Context()) supplierId = (await SeedAsync(seed)).Id;
        async Task<Result<RequestJitRiskAnalysisResult>> SendAsync(RequestJitRiskAnalysisCommand command)
        {
            await using var context = store.Context();
            await using var unit = new EfUnitOfWork(context);
            var handler = new RequestJitRiskAnalysisCommandHandler(new SupplierRepository(context),
                new SupplierRiskIndicatorReader(context), new SimulatedSupplierPerformanceService(),
                new SimulatedFinancialIntegrationService(), new SupplierRiskFeaturesValidator(),
                new OutboxWriter(context, new IntegrationMessageSerializer()), unit, new FixedTimeProvider(Now),
                new XAIAuditLogRepository(context), new RiskManagementRepository(context), new IntegrationMessageSerializer());
            return await handler.HandleAsync(command);
        }
        var accepted = await SendAsync(Command(supplierId));
        var replay = await SendAsync(Command(supplierId));
        Assert.True(accepted.IsSuccess);
        Assert.Equal(accepted.Value, replay.Value);
        var conflict = await SendAsync(Command(supplierId) with { OnTimeDeliveryRate = .5m });
        Assert.Equal("RiskAnalysis.CorrelationConflict", Assert.Single(conflict.Errors).Code);
        await using var verification = store.Context();
        Assert.Equal(1, await verification.XAIAuditLogs.CountAsync());
        Assert.Equal(1, await verification.OutboxMessages.CountAsync());
    }

    private static async Task<Supplier> SeedAsync(SupplyGuardDbContext context)
    {
        var supplier = new Supplier("JIT Supplier", "JIT-1", "TR");
        var other = new Supplier("Other Supplier", "JIT-2", "TR");
        context.Suppliers.AddRange(supplier, other);
        for (var index = 0; index < 3; index++) context.RiskIndicators.Add(Indicator(supplier.Id));
        var inactive = Indicator(supplier.Id);
        inactive.Deactivate();
        var deleted = Indicator(supplier.Id);
        deleted.MarkAsDeleted();
        context.RiskIndicators.AddRange(inactive, deleted, Indicator(other.Id));
        await context.SaveChangesAsync();
        return supplier;
    }

    private static RiskIndicator Indicator(Guid supplierId) => new(supplierId, RiskCategory.Delivery,
        Guid.NewGuid().ToString("N"), "Delay", RiskLevel.Medium, 1m, 20m, 1m, "Demo", Now);

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class Store(SqliteConnection connection, IModel model) : IAsyncDisposable
    {
        public static async Task<Store> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var modelContext = new ModelContext(new DbContextOptionsBuilder<ModelContext>().UseSqlite(connection).Options);
            var store = new Store(connection, modelContext.Model);
            await using var context = store.Context();
            await context.Database.EnsureCreatedAsync();
            return store;
        }
        public SupplyGuardDbContext Context() => new(new DbContextOptionsBuilder<SupplyGuardDbContext>()
            .UseSqlite(connection).UseModel(model).Options);
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class ModelContext(DbContextOptions<ModelContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfiguration(new SupplierConfiguration());
            builder.ApplyConfiguration(new RiskAssessmentConfiguration());
            builder.ApplyConfiguration(new RiskScoreConfiguration());
            builder.ApplyConfiguration(new RiskIndicatorConfiguration());
            builder.ApplyConfiguration(new EarlyWarningConfiguration());
            builder.ApplyConfiguration(new XAIAuditLogConfiguration());
            builder.ApplyConfiguration(new OutboxMessageConfiguration());
            var converter = new ValueConverter<DateTimeOffset, long>(value => value.UtcTicks, value => new(value, TimeSpan.Zero));
            foreach (var property in builder.Model.GetEntityTypes().SelectMany(type => type.GetProperties()))
            {
                if ((Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType) != typeof(DateTimeOffset)) continue;
                property.SetValueConverter(converter);
                property.SetColumnType("INTEGER");
            }
        }
    }
}
