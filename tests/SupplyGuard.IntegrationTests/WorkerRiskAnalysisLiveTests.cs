using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisResponse;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using SupplyGuard.Infrastructure.Persistence.Repositories;
using Xunit;

namespace SupplyGuard.IntegrationTests;

/// <summary>Only point the test connection at an isolated disposable PostgreSQL database.</summary>
public sealed class WorkerRiskAnalysisLiveTests
{
    private static SupplyGuardDbContext Context(params IInterceptor[] interceptors) => new(
        new DbContextOptionsBuilder<SupplyGuardDbContext>()
            .UseNpgsql(Environment.GetEnvironmentVariable("SUPPLYGUARD_WORKER_TEST_POSTGRES"))
            .AddInterceptors(interceptors).Options);

    [WorkerInfrastructureFact]
    public async Task Postgres_AuditLockSerializesResponses_AndOutboxSkipLockedDoesNotDoublePublish()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        await using var first = Context();
        await first.Database.MigrateAsync(timeout.Token);
        var supplier = new Supplier("Lock test", Guid.NewGuid().ToString("N"), "TR");
        var request = WorkerRiskAnalysisFixtures.Request(supplier.Id) with { CorrelationId = Guid.NewGuid().ToString("D") };
        var response = WorkerRiskAnalysisFixtures.Response(request);
        first.Suppliers.Add(supplier);
        first.XAIAuditLogs.Add(XAIAuditLog.CreatePending(supplier.Id, request.CorrelationId, request.MessageId,
            new IntegrationMessageSerializer().Serialize(request), request.OccurredAtUtc));
        await new OutboxWriter(first, new IntegrationMessageSerializer()).WriteAsync(request, timeout.Token);
        await first.SaveChangesAsync(timeout.Token);
        first.ChangeTracker.Clear();

        await using var unit = new EfUnitOfWork(first);
        await unit.BeginTransactionAsync(timeout.Token);
        var locked = await new XAIAuditLogRepository(first).GetByCorrelationIdForUpdateAsync(request.CorrelationId, timeout.Token);
        var commandStarted = new LockCommandObserver();
        await using var second = Context(commandStarted);
        await using var secondUnit = new EfUnitOfWork(second);
        var competing = new ProcessRiskAnalysisResponseCommandHandler(new XAIAuditLogRepository(second),
            new RiskAnalysisResponseSerializer(), secondUnit, TimeProvider.System)
            .HandleAsync(new(response), timeout.Token);
        await commandStarted.Started.Task.WaitAsync(timeout.Token);
        await Task.Delay(100, timeout.Token);
        Assert.False(competing.IsCompleted);
        locked!.Complete(response.ConfidenceScore, response.ExplanationText,
            new RiskAnalysisResponseSerializer().Serialize(response), DateTimeOffset.UtcNow);
        await unit.CommitAsync(timeout.Token);
        Assert.Equal(RiskAnalysisResponseDisposition.Duplicate, (await competing).Value!.Disposition);

        var publisher = new Mock<IMessagePublisher>();
        publisher.Setup(x => x.PublishAsync(It.IsAny<SupplyGuard.Application.Common.Messaging.IIntegrationMessage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var services = new ServiceCollection();
        services.AddScoped(_ => Context());
        services.AddSingleton<IIntegrationMessageSerializer, IntegrationMessageSerializer>();
        services.AddSingleton(publisher.Object);
        await using var provider = services.BuildServiceProvider();
        var dispatcher = new OutboxDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
            Options.Create(new OutboxProcessorOptions()), TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);
        await using (var claiming = Context())
        {
            await using var claim = await claiming.Database.BeginTransactionAsync(timeout.Token);
            await claiming.OutboxMessages.FromSqlInterpolated($"""
                SELECT * FROM supplyguard.outbox_messages WHERE "Id" = {request.MessageId} FOR UPDATE
                """).ToListAsync(timeout.Token);
            Assert.False(await dispatcher.TryDispatchAsync(request.MessageId, timeout.Token));
            publisher.Verify(x => x.PublishAsync(It.IsAny<SupplyGuard.Application.Common.Messaging.IIntegrationMessage>(),
                It.IsAny<CancellationToken>()), Times.Never);
            await claim.RollbackAsync(timeout.Token);
        }
        Assert.True(await dispatcher.TryDispatchAsync(request.MessageId, timeout.Token));
        Assert.True(await dispatcher.TryDispatchAsync(request.MessageId, timeout.Token));
        publisher.Verify(x => x.PublishAsync(It.IsAny<SupplyGuard.Application.Common.Messaging.IIntegrationMessage>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [WorkerInfrastructureFact]
    public async Task RealBrokerAndPostgres_OutboxToFlatRequest_ThenResponseDuplicateAndPoisonAreHandled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var suffix = Guid.NewGuid().ToString("N");
        var settings = new RabbitMqOptions
        {
            HostName = Environment.GetEnvironmentVariable("RabbitMq__HostName") ?? "localhost",
            Port = int.Parse(Environment.GetEnvironmentVariable("RabbitMq__Port") ?? "5672"),
            UserName = Environment.GetEnvironmentVariable("RabbitMq__UserName") ?? "worker_test",
            Password = Environment.GetEnvironmentVariable("RabbitMq__Password") ?? throw new InvalidOperationException("Set RabbitMq__Password."),
            VirtualHost = Environment.GetEnvironmentVariable("RabbitMq__VirtualHost") ?? "worker_tests",
            WorkerRequestQueueName = $"supplyguard.test.{suffix}.requests",
            WorkerResponseQueueName = $"supplyguard.test.{suffix}.responses",
            WorkerResponseErrorQueueName = $"supplyguard.test.{suffix}.errors",
            ExchangeName = $"supplyguard.test.{suffix}.events",
            NotificationQueueName = $"supplyguard.test.{suffix}.notification"
        };
        var factory = new ConnectionFactory
        {
            HostName = settings.HostName, Port = settings.Port, UserName = settings.UserName,
            Password = settings.Password, VirtualHost = settings.VirtualHost, AutomaticRecoveryEnabled = false
        };
        var options = Options.Create(settings);
        var connections = new RabbitMqConnectionRetry(factory, options, TimeProvider.System, NullLogger<RabbitMqConnectionRetry>.Instance);
        await using var publisher = new RabbitMqMessagePublisher(factory, options, TimeProvider.System,
            NullLogger<RabbitMqMessagePublisher>.Instance, new IntegrationMessageSerializer(), connections);
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddScoped(_ => Context());
        services.AddScoped<IXAIAuditLogRepository, XAIAuditLogRepository>();
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddSingleton<IRiskAnalysisResponseSerializer, RiskAnalysisResponseSerializer>();
        services.AddSingleton<IIntegrationMessageSerializer, IntegrationMessageSerializer>();
        services.AddSingleton<IMessagePublisher>(publisher);
        await using var provider = services.BuildServiceProvider();
        var logger = new ConsumerLog();
        using var consumer = new RiskAnalysisResponseConsumer(connections, provider.GetRequiredService<IServiceScopeFactory>(),
            new RiskAnalysisResponseSerializer(), options, TimeProvider.System, logger);
        await using var inspectionConnection = await connections.ConnectAsync(timeout.Token);
        await using var channel = await inspectionConnection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), timeout.Token);
        await using (var seed = Context()) await seed.Database.MigrateAsync(timeout.Token);
        await consumer.StartAsync(timeout.Token);
        try
        {
            await logger.Ready.Task.WaitAsync(timeout.Token);
            var supplier = new Supplier("Live worker", suffix, "TR");
            var request = WorkerRiskAnalysisFixtures.Request(supplier.Id) with { CorrelationId = suffix };
            await using (var seed = Context())
            {
                seed.Suppliers.Add(supplier);
                seed.XAIAuditLogs.Add(XAIAuditLog.CreatePending(supplier.Id, request.CorrelationId, request.MessageId,
                    new IntegrationMessageSerializer().Serialize(request), request.OccurredAtUtc));
                await new OutboxWriter(seed, new IntegrationMessageSerializer()).WriteAsync(request, timeout.Token);
                await seed.SaveChangesAsync(timeout.Token);
            }
            var dispatcher = new OutboxDispatcher(provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new OutboxProcessorOptions()), TimeProvider.System, NullLogger<OutboxDispatcher>.Instance);
            Assert.True(await dispatcher.TryDispatchAsync(request.MessageId, timeout.Token));
            var delivered = await channel.BasicGetAsync(settings.WorkerRequestQueueName, autoAck: false, timeout.Token);
            Assert.NotNull(delivered);
            var flat = new IntegrationMessageSerializer().Deserialize<SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1.RiskAnalysisRequest>(
                Encoding.UTF8.GetString(delivered!.Body.Span));
            Assert.Equal(request.CorrelationId, flat.CorrelationId);
            Assert.Equal(request.QualityDefectRate, flat.QualityDefectRate);
            var response = WorkerRiskAnalysisFixtures.Response(request) with { ConfidenceScore = .996123456m };
            var body = Encoding.UTF8.GetBytes(new RiskAnalysisResponseSerializer().Serialize(response));
            // Python supplies neither MessageId nor Type on responses.
            var properties = new BasicProperties { Persistent = true, ContentType = "application/json", ContentEncoding = "utf-8", CorrelationId = request.CorrelationId };
            await channel.BasicPublishAsync("", settings.WorkerResponseQueueName, true, properties, body, timeout.Token);
            await channel.BasicAckAsync(delivered.DeliveryTag, false, timeout.Token);
            await logger.Processed.Task.WaitAsync(timeout.Token);
            DateTimeOffset? completedAt;
            await using (var verification = Context())
            {
                var audit = await verification.XAIAuditLogs.SingleAsync(x => x.CorrelationId == request.CorrelationId, timeout.Token);
                Assert.Equal(ExplanationStatus.Completed, audit.ExplanationStatus);
                Assert.Equal(.9961m, audit.ConfidenceScore);
                Assert.Equal(response, new RiskAnalysisResponseSerializer().Deserialize(audit.ResponsePayload!));
                completedAt = audit.CompletedAtUtc;
            }
            await channel.BasicPublishAsync("", settings.WorkerResponseQueueName, true, properties, body, timeout.Token);
            await logger.Duplicate.Task.WaitAsync(timeout.Token);
            await using (var verification = Context())
                Assert.Equal(completedAt, (await verification.XAIAuditLogs.SingleAsync(x => x.CorrelationId == request.CorrelationId, timeout.Token)).CompletedAtUtc);
            await channel.BasicPublishAsync("", settings.WorkerResponseQueueName, true, properties, Encoding.UTF8.GetBytes("{}"), timeout.Token);
            await logger.Quarantined.Task.WaitAsync(timeout.Token);
            var poison = await channel.BasicGetAsync(settings.WorkerResponseErrorQueueName, true, timeout.Token);
            Assert.NotNull(poison);
            Assert.True(poison!.BasicProperties.Persistent);
            Assert.Equal("{}", Encoding.UTF8.GetString(poison.Body.Span));
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await consumer.StopAsync(cleanup.Token);
            foreach (var queue in new[] { settings.WorkerRequestQueueName, settings.WorkerResponseQueueName, settings.WorkerResponseErrorQueueName, settings.NotificationQueueName })
                await channel.QueueDeleteAsync(queue, cancellationToken: cleanup.Token);
            await channel.ExchangeDeleteAsync(settings.ExchangeName, cancellationToken: cleanup.Token);
        }
    }

    private sealed class LockCommandObserver : DbCommandInterceptor
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override ValueTask<InterceptionResult<System.Data.Common.DbDataReader>> ReaderExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<System.Data.Common.DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FOR UPDATE", StringComparison.Ordinal)) Started.TrySetResult();
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ConsumerLog : ILogger<RiskAnalysisResponseConsumer>
    {
        public TaskCompletionSource Ready { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Processed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Duplicate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Quarantined { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            var text = formatter(state, error);
            if (text.Contains("consumer subscribed", StringComparison.Ordinal)) Ready.TrySetResult();
            if (text.Contains("handled with Processed", StringComparison.Ordinal)) Processed.TrySetResult();
            if (text.Contains("handled with Duplicate", StringComparison.Ordinal)) Duplicate.TrySetResult();
            if (text.Contains("quarantined after", StringComparison.Ordinal)) Quarantined.TrySetResult();
        }
    }
}

public sealed class WorkerInfrastructureFactAttribute : FactAttribute
{
    public WorkerInfrastructureFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("SUPPLYGUARD_WORKER_TEST_POSTGRES") is null)
            Skip = "Set SUPPLYGUARD_WORKER_TEST_POSTGRES to a disposable database and RabbitMq__* for an isolated broker.";
    }
}
