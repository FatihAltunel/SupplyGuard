using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Configurations;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using Xunit;

namespace SupplyGuard.IntegrationTests;

// SQLite verifies persisted workflow state; PostgreSQL-specific behavior still needs a live broker/database check.
public class OutboxDispatcherTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 16, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Dispatch_OnlyDuePendingBatch_IsSavedAfterConfirmation()
    {
        await using var store = await TestStore.CreateAsync();
        var oldest = CreateRow(Now.AddMinutes(-4));
        var newer = CreateRow(Now.AddMinutes(-3));
        var delayed = CreateRow(Now.AddMinutes(-5));
        delayed.RecordFailure("Retry later", Now.AddHours(1));
        var processed = CreateRow(Now.AddMinutes(-6));
        processed.MarkAsProcessed(Now.AddMinutes(-1));
        await store.SeedAsync(oldest, newer, delayed, processed);
        var enteredPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var confirmed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new RecordingPublisher(async (_, cancellationToken) =>
        {
            enteredPublish.TrySetResult();
            await confirmed.Task.WaitAsync(cancellationToken);
        });
        await using var context = store.CreateContext();
        var dispatcher = CreateDispatcher(context, publisher, batchSize: 1);

        var dispatch = dispatcher.DispatchBatchAsync();
        await enteredPublish.Task.WaitAsync(TimeSpan.FromSeconds(5));
        await using (var inspection = store.CreateContext())
        {
            Assert.Null((await inspection.OutboxMessages.SingleAsync(row => row.Id == oldest.Id)).ProcessedAtUtc);
        }

        confirmed.TrySetResult();
        await dispatch.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(oldest.Id, Assert.Single(publisher.Messages).MessageId);
        await using var verification = store.CreateContext();
        Assert.Equal(Now, (await verification.OutboxMessages.SingleAsync(row => row.Id == oldest.Id)).ProcessedAtUtc);
        Assert.Null((await verification.OutboxMessages.SingleAsync(row => row.Id == newer.Id)).ProcessedAtUtc);
        Assert.Null((await verification.OutboxMessages.SingleAsync(row => row.Id == delayed.Id)).ProcessedAtUtc);
        Assert.Equal(Now.AddMinutes(-1), (await verification.OutboxMessages.SingleAsync(row => row.Id == processed.Id)).ProcessedAtUtc);
    }

    [Fact]
    public async Task Dispatch_FailureSchedulesRetry_AndDoesNotBlockHealthyMessage()
    {
        await using var store = await TestStore.CreateAsync();
        var failing = CreateRow(Now.AddMinutes(-2));
        var healthy = CreateRow(Now.AddMinutes(-1));
        await store.SeedAsync(failing, healthy);
        var publisher = new RecordingPublisher((message, _) => message.MessageId == failing.Id
            ? Task.FromException(new InvalidOperationException("Sensitive provider detail"))
            : Task.CompletedTask);
        await using (var context = store.CreateContext())
        {
            var dispatcher = CreateDispatcher(context, publisher);
            await dispatcher.DispatchBatchAsync();
            await dispatcher.DispatchBatchAsync();
        }

        Assert.Equal(2, publisher.Messages.Count);
        await using (var verification = store.CreateContext())
        {
            var failure = await verification.OutboxMessages.SingleAsync(row => row.Id == failing.Id);
            Assert.Null(failure.ProcessedAtUtc);
            Assert.Equal(1, failure.RetryCount);
            Assert.Equal(Now.AddSeconds(30), failure.NextAttemptAtUtc);
            Assert.DoesNotContain("Sensitive provider detail", failure.LastError!);
            Assert.Equal(Now, (await verification.OutboxMessages.SingleAsync(row => row.Id == healthy.Id)).ProcessedAtUtc);
        }

        var retryPublisher = new RecordingPublisher();
        await using var retryContext = store.CreateContext();
        await CreateDispatcher(retryContext, retryPublisher, now: Now.AddSeconds(30)).DispatchBatchAsync();
        Assert.Equal(failing.Id, Assert.Single(retryPublisher.Messages).MessageId);
        await using var finalVerification = store.CreateContext();
        Assert.Equal(Now.AddSeconds(30), (await finalVerification.OutboxMessages.SingleAsync(row => row.Id == failing.Id)).ProcessedAtUtc);
    }

    [Theory]
    [InlineData("unknown")]
    [InlineData("malformed")]
    [InlineData("mismatched")]
    public async Task Dispatch_InvalidMessage_RemainsPendingWithoutPublication(string scenario)
    {
        await using var store = await TestStore.CreateAsync();
        var row = CreateRow(Now.AddMinutes(-1));
        var payload = scenario == "malformed" ? "{broken" : row.Payload;
        var messageType = scenario == "unknown" ? "unknown.v1" : row.MessageType;
        var id = scenario == "mismatched" ? Guid.NewGuid() : row.Id;
        await store.SeedAsync(new OutboxMessage(id, messageType, payload, row.CorrelationId, row.OccurredAtUtc));
        var publisher = new RecordingPublisher();
        await using var context = store.CreateContext();

        await CreateDispatcher(context, publisher).DispatchBatchAsync();

        Assert.Empty(publisher.Messages);
        await using var verification = store.CreateContext();
        var pending = await verification.OutboxMessages.SingleAsync();
        Assert.Null(pending.ProcessedAtUtc);
        Assert.Equal(1, pending.RetryCount);
    }

    [Fact]
    public async Task Dispatch_SaveFailureAfterConfirmation_AbortsBatchAndAllowsRedelivery()
    {
        await using var store = await TestStore.CreateAsync();
        var first = CreateRow(Now.AddMinutes(-2));
        var second = CreateRow(Now.AddMinutes(-1));
        await store.SeedAsync(first, second);
        var publisher = new RecordingPublisher();
        await using (var failingContext = store.CreateContext(new FailingSaveInterceptor()))
        {
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                CreateDispatcher(failingContext, publisher).DispatchBatchAsync());
        }

        Assert.Equal(first.Id, Assert.Single(publisher.Messages).MessageId);
        await using (var verification = store.CreateContext())
        {
            Assert.All(await verification.OutboxMessages.ToListAsync(), row => Assert.Null(row.ProcessedAtUtc));
        }

        await using (var freshContext = store.CreateContext())
        {
            await CreateDispatcher(freshContext, publisher).DispatchBatchAsync();
        }

        Assert.Equal(new[] { first.Id, first.Id, second.Id }, publisher.Messages.Select(message => message.MessageId));
        await using var finalVerification = store.CreateContext();
        Assert.All(await finalVerification.OutboxMessages.ToListAsync(), row => Assert.Equal(Now, row.ProcessedAtUtc));
    }

    [Fact]
    public async Task Dispatch_ShutdownDuringPublication_LeavesMessagePendingWithoutFailureCount()
    {
        await using var store = await TestStore.CreateAsync();
        await store.SeedAsync(CreateRow(Now.AddMinutes(-1)));
        using var cancellation = new CancellationTokenSource();
        var enteredPublish = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var publisher = new RecordingPublisher(async (_, token) =>
        {
            enteredPublish.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
        });
        await using var context = store.CreateContext();
        var dispatch = CreateDispatcher(context, publisher).DispatchBatchAsync(cancellation.Token);
        await enteredPublish.Task.WaitAsync(TimeSpan.FromSeconds(5));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);

        await using var verification = store.CreateContext();
        var pending = await verification.OutboxMessages.SingleAsync();
        Assert.Null(pending.ProcessedAtUtc);
        Assert.Equal(0, pending.RetryCount);
    }

    [Fact]
    public async Task HostedProcessor_CreatesAndDisposesFreshScopePerIteration()
    {
        await using var store = await TestStore.CreateAsync();
        var contexts = new List<SupplyGuardDbContext>();
        var secondDisposal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposalCount = 0;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ =>
        {
            var context = store.CreateContext();
            contexts.Add(context);
            return context;
        });
        services.AddScoped<IMessagePublisher>(provider => new ScopeTrackingPublisher(
            provider.GetRequiredService<SupplyGuardDbContext>(), () =>
            {
                if (Interlocked.Increment(ref disposalCount) >= 2)
                {
                    secondDisposal.TrySetResult();
                }
            }));
        services.AddOutboxProcessing(new ConfigurationBuilder().Build());
        services.Configure<OutboxProcessorOptions>(settings => settings.PollInterval = TimeSpan.FromMilliseconds(10));
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var worker = Assert.IsType<OutboxProcessor>(Assert.Single(provider.GetServices<IHostedService>()));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await secondDisposal.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        finally
        {
            using var stopTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await worker.StopAsync(stopTimeout.Token);
        }

        Assert.True(contexts.Count >= 2);
        Assert.Equal(contexts.Count, contexts.Select(context => context.ContextId.InstanceId).Distinct().Count());
        Assert.Equal(contexts.Count, disposalCount);
        Assert.All(contexts, context => Assert.Throws<ObjectDisposedException>(() => _ = context.ChangeTracker));
    }

    private static OutboxDispatcher CreateDispatcher(
        SupplyGuardDbContext context, IMessagePublisher publisher, int batchSize = 20, DateTimeOffset? now = null) =>
        new(context, publisher, Options.Create(new OutboxProcessorOptions { BatchSize = batchSize }),
            new FixedTimeProvider(now ?? Now), NullLogger<OutboxDispatcher>.Instance);

    private static OutboxMessage CreateRow(DateTimeOffset occurredAtUtc)
    {
        var message = new EarlyWarningRaisedV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "Critical", "dispatch-test", occurredAtUtc);
        return new OutboxMessage(message.MessageId, message.MessageType, JsonSerializer.Serialize(message),
            message.CorrelationId, message.OccurredAtUtc);
    }

    private sealed class RecordingPublisher(Func<IIntegrationMessage, CancellationToken, Task>? onPublish = null) : IMessagePublisher
    {
        public List<IIntegrationMessage> Messages { get; } = [];

        public Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
        {
            Messages.Add(message);
            return onPublish?.Invoke(message, cancellationToken) ?? Task.CompletedTask;
        }
    }

    private sealed class ScopeTrackingPublisher(SupplyGuardDbContext context, Action onDispose) : IMessagePublisher, IDisposable
    {
        public Task PublishAsync(IIntegrationMessage message, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose()
        {
            _ = context.ContextId;
            onDispose();
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FailingSaveInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new DbUpdateException("Simulated completion-save failure");
    }

    private sealed class TestStore(SqliteConnection connection, IModel model) : IAsyncDisposable
    {
        public static async Task<TestStore> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            await using var modelContext = new TestModelContext(
                new DbContextOptionsBuilder<TestModelContext>().UseSqlite(connection).Options);
            var store = new TestStore(connection, modelContext.Model);
            await using var context = store.CreateContext();
            await context.Database.EnsureCreatedAsync();
            return store;
        }

        public SupplyGuardDbContext CreateContext(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<SupplyGuardDbContext>()
                .UseSqlite(connection).UseModel(model);
            if (interceptor is not null)
            {
                options.AddInterceptors(interceptor);
            }

            return new SupplyGuardDbContext(options.Options);
        }

        public async Task SeedAsync(params OutboxMessage[] messages)
        {
            await using var context = CreateContext();
            context.OutboxMessages.AddRange(messages);
            await context.SaveChangesAsync();
        }

        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }

    private sealed class TestModelContext(DbContextOptions<TestModelContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());
            // SQLite cannot order DateTimeOffset natively. Store UTC ticks for these workflow tests only.
            var converter = new ValueConverter<DateTimeOffset, long>(
                value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
            var entity = modelBuilder.Entity<OutboxMessage>();
            entity.Property(row => row.OccurredAtUtc).HasConversion(converter).HasColumnType("INTEGER");
            entity.Property(row => row.NextAttemptAtUtc).HasConversion(converter).HasColumnType("INTEGER");
            entity.Property(row => row.ProcessedAtUtc).HasConversion(converter).HasColumnType("INTEGER");
        }
    }
}
