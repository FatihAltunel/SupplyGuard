using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Caching;
using SupplyGuard.Application.Common.Exceptions;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;
using SupplyGuard.Application.Features.RiskManagement.RequestRiskAnalysis;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Tests;

public sealed class RiskAnalysisUseCaseTests
{
    [Fact]
    public async Task Request_CommitsBeforeImmediateDispatchAndPreservesIdentifiers()
    {
        var store = new Store();
        var result = await RequestHandler(store).HandleAsync(new(store.Supplier.Id, RiskAnalysisContractTests.Requested().Input));

        Assert.True(result.IsSuccess);
        var message = Assert.IsType<RiskAnalysisRequestedEvent>(Assert.Single(store.Messages));
        Assert.Equal(["begin", "stage-request", "commit", "dispatch"], store.Operations);
        Assert.NotEqual(Guid.Empty, message.CorrelationId);
        Assert.NotEqual(message.CorrelationId, message.MessageId);
        Assert.Equal(message.CorrelationId, result.Value!.CorrelationId);
        Assert.Equal(message.MessageId, result.Value.RequestMessageId);
        Assert.Equal(message.MessageId, store.DispatchedId);
        Assert.True(RiskAnalysisContractRules.IsUtcMilliseconds(message.OccurredAtUtc));
        Assert.Empty(store.Audits);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_RemainsAcceptedWhenImmediateDispatchFailsAfterCommit(bool cancelled)
    {
        var store = new Store { DispatchFailure = cancelled ? new OperationCanceledException() : new IOException("Broker unavailable") };
        var result = await RequestHandler(store).HandleAsync(new(store.Supplier.Id, RiskAnalysisContractTests.Requested().Input));
        Assert.True(result.IsSuccess);
        Assert.Equal(1, store.CommitCount);
        Assert.Equal(0, store.RollbackCount);
        Assert.Single(store.Messages);
    }

    [Fact]
    public async Task Request_FailedCommitRollsBackWithoutPublishing()
    {
        var store = new Store { CommitFailure = new IOException("Database unavailable") };
        await Assert.ThrowsAsync<IOException>(() => RequestHandler(store).HandleAsync(new(
            store.Supplier.Id, RiskAnalysisContractTests.Requested().Input)));
        Assert.Equal(1, store.RollbackCount);
        Assert.DoesNotContain("dispatch", store.Operations);
    }

    [Fact]
    public async Task Request_MissingSupplierDoesNotStageAnything()
    {
        var store = new Store();
        var result = await RequestHandler(store).HandleAsync(new(Guid.NewGuid(), RiskAnalysisContractTests.Requested().Input));
        Assert.False(result.IsSuccess);
        Assert.Empty(store.Messages);
        Assert.Empty(store.Operations);
    }

    [Fact]
    public async Task Request_OversizedPayloadDoesNotBeginTransaction()
    {
        var store = new Store();
        var input = RiskAnalysisContractTests.Requested().Input with
        {
            Features = new Dictionary<string, double?> { [new string('a', 100_001)] = 1 }
        };
        var result = await RequestHandler(store).HandleAsync(new(store.Supplier.Id, input));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "RiskAnalysis.PayloadTooLarge");
        Assert.Empty(store.Operations);
    }

    [Fact]
    public async Task Success_AppliesDomainAssessmentAndExactlyOneTerminalAudit()
    {
        var store = new Store();
        var completion = CompletionFor(store);
        var result = await CompletionHandler(store).HandleAsync(new(completion));

        Assert.True(result.IsSuccess);
        Assert.Equal(RiskAnalysisCompletionDisposition.Processed, result.Value!.Disposition);
        var assessment = Assert.Single(store.Supplier.RiskAssessments);
        var audit = Assert.Single(store.Audits);
        Assert.Equal(65m, assessment.OverallRiskScore);
        Assert.Equal(RiskLevel.High, assessment.OverallRiskLevel);
        Assert.Empty(assessment.RiskScores);
        Assert.Equal(assessment.Id, audit.RiskAssessmentId);
        Assert.Equal(assessment.Id, result.Value.RiskAssessmentId);
        Assert.Equal(completion.CorrelationId.ToString("D"), audit.CorrelationId);
        Assert.Equal(ExplanationStatus.Completed, audit.ExplanationStatus);
        Assert.Equal(store.Snapshot!.Payload, audit.RequestPayload);
        Assert.Contains("\"isMock\":true", audit.ResponsePayload!);
        Assert.Same(audit, Assert.Single(store.Supplier.XAIAuditLogs));
        Assert.Equal(1, store.CommitCount);
        Assert.Empty(store.Supplier.DomainEvents);
    }

    [Fact]
    public async Task TerminalFailure_OnlyInsertsFailureAudit()
    {
        var store = new Store();
        var completion = CompletionFor(store) with
        {
            Status = RiskAnalysisContractRules.Failed,
            Result = null,
            Error = new("DUMMY_PROCESSING_FAILED", "The dummy worker could not complete the analysis.")
        };
        var result = await CompletionHandler(store).HandleAsync(new(completion));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.RiskAssessmentId);
        Assert.Empty(store.Supplier.RiskAssessments);
        Assert.Empty(store.Supplier.XAIAuditLogs);
        Assert.Null(store.Supplier.LastModifiedAtUtc);
        var audit = Assert.Single(store.Audits);
        Assert.False(audit.IsSuccessful);
        Assert.Equal(ExplanationStatus.Failed, audit.ExplanationStatus);
        Assert.Equal(0m, audit.ConfidenceScore);
        Assert.Equal("DUMMY_PROCESSING_FAILED", audit.FailureCode);
        Assert.Null(audit.RiskAssessmentId);
        Assert.Equal(1, store.CommitCount);
    }

    [Fact]
    public async Task ExistingCorrelation_IsDuplicateBeforeLoadingRequestOrSupplier()
    {
        var store = new Store { ExistingAudit = true };
        var completion = CompletionFor(store);
        var result = await CompletionHandler(store).HandleAsync(new(completion));
        Assert.Equal(RiskAnalysisCompletionDisposition.Duplicate, result.Value!.Disposition);
        Assert.Equal(0, store.SnapshotReads);
        Assert.Equal(0, store.SupplierLoads);
        Assert.Equal(0, store.CommitCount);
        Assert.Empty(store.Audits);
    }

    [Fact]
    public async Task DuplicateCommittedWhileAcquiringSupplierLock_IsRechecked()
    {
        var store = new Store { AuditAppearsOnSecondCheck = true };
        var result = await CompletionHandler(store).HandleAsync(new(CompletionFor(store)));
        Assert.Equal(RiskAnalysisCompletionDisposition.Duplicate, result.Value!.Disposition);
        Assert.Equal(1, store.RollbackCount);
        Assert.Empty(store.Audits);
        Assert.Empty(store.Supplier.RiskAssessments);
    }

    [Fact]
    public async Task UniqueCorrelationConflict_IsRolledBackAndReturnedAsDuplicate()
    {
        var store = new Store();
        var completion = CompletionFor(store);
        store.CommitFailure = new DuplicateCorrelationException(completion.CorrelationId);
        var result = await CompletionHandler(store).HandleAsync(new(completion));
        Assert.Equal(RiskAnalysisCompletionDisposition.Duplicate, result.Value!.Disposition);
        Assert.Equal(1, store.RollbackCount);
    }

    [Fact]
    public async Task UnrelatedCommitFailure_IsRolledBackAndPropagated()
    {
        var store = new Store { CommitFailure = new IOException("Database unavailable") };
        await Assert.ThrowsAsync<IOException>(() => CompletionHandler(store).HandleAsync(new(CompletionFor(store))));
        Assert.Equal(1, store.RollbackCount);
    }

    [Fact]
    public async Task MismatchedRequest_DoesNotMutateSupplierOrCommit()
    {
        var store = new Store();
        var completion = CompletionFor(store) with { SupplierId = Guid.NewGuid() };
        var result = await CompletionHandler(store).HandleAsync(new(completion));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "RiskAnalysis.RequestMismatch");
        Assert.Empty(store.Supplier.RiskAssessments);
        Assert.Empty(store.Audits);
        Assert.Equal(0, store.CommitCount);
    }

    [Fact]
    public async Task MissingRequest_IsRejectedWithoutTransaction()
    {
        var store = new Store();
        var completion = CompletionFor(store);
        store.Snapshot = null;
        var result = await CompletionHandler(store).HandleAsync(new(completion));
        Assert.False(result.IsSuccess);
        Assert.Contains(result.Errors, error => error.Code == "RiskAnalysis.RequestNotFound");
        Assert.Empty(store.Operations);
    }

    [Fact]
    public async Task PureDiPipeline_ValidatesBeforePersistenceAndInvalidatesAfterCommit()
    {
        var store = new Store();
        var cache = new FailingCache(store);
        var services = new ServiceCollection();
        services.AddApplication();
        services.AddSingleton<ISupplierRepository>(store);
        services.AddSingleton<IOutboxWriter>(store);
        services.AddSingleton<IOutboxReader>(store);
        services.AddSingleton<IOutboxDispatcher>(store);
        services.AddSingleton<IUnitOfWork>(store);
        services.AddSingleton<IRiskAnalysisRepository>(store);
        services.AddSingleton<IIntegrationMessageSerializer>(store.Serializer);
        services.AddSingleton<IDistributedCache>(cache);
        services.AddSingleton<ILogger<CreateRequestRiskAnalysisCommandHandler>>(NullLogger<CreateRequestRiskAnalysisCommandHandler>.Instance);
        services.AddSingleton<ILogger<CacheInvalidatingCommandHandlerDecorator<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>>>(
            NullLogger<CacheInvalidatingCommandHandlerDecorator<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>>.Instance);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>>();
        var completion = CompletionFor(store);

        var invalid = await handler.HandleAsync(new(completion with { Result = null }));
        Assert.False(invalid.IsSuccess);
        Assert.Equal(0, store.AuditChecks);

        var processed = await handler.HandleAsync(new(completion));
        Assert.True(processed.IsSuccess);
        Assert.Equal(CacheKeys.SupplierDetails(store.Supplier.Id), Assert.Single(cache.RemovedKeys));
        Assert.Equal("cache", store.Operations.Last());

        store.ExistingAudit = true;
        var duplicate = await handler.HandleAsync(new(completion));
        Assert.Equal(RiskAnalysisCompletionDisposition.Duplicate, duplicate.Value!.Disposition);
        Assert.Equal(2, cache.RemovedKeys.Count);
    }

    private static CreateRequestRiskAnalysisCommandHandler RequestHandler(Store store) => new(
        store, store, store, store.Serializer, store, new MillisecondTimeProvider(),
        NullLogger<CreateRequestRiskAnalysisCommandHandler>.Instance);

    private static CreateProcessRiskAnalysisCompletedCommandHandler CompletionHandler(Store store) => new(
        store, store, store.Serializer,
        new RiskAnalysisRequestedEventValidator(new RiskAnalysisInputV1Validator()), store);

    private static RiskAnalysisCompletedEvent CompletionFor(Store store)
    {
        var completion = RiskAnalysisContractTests.Completed() with { SupplierId = store.Supplier.Id };
        var requested = RiskAnalysisContractTests.Requested() with { SupplierId = store.Supplier.Id };
        store.Snapshot = new OutboxMessage(
            requested.MessageId, requested.MessageType, store.Serializer.Serialize(requested),
            requested.CorrelationId.ToString("D"), requested.OccurredAtUtc);
        return completion;
    }

    private sealed class MillisecondTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 10, 31, 12, 0, 0, TimeSpan.Zero).AddTicks(12345);
    }

    // A serialization test double, not the deferred strict Infrastructure serializer.
    private sealed class TestSerializer : IIntegrationMessageSerializer
    {
        public string Serialize(IIntegrationMessage message) => JsonSerializer.Serialize(message, message.GetType());
        public TMessage Deserialize<TMessage>(string payload) where TMessage : class, IIntegrationMessage =>
            JsonSerializer.Deserialize<TMessage>(payload) ?? throw new JsonException("Null integration message.");
    }

    private sealed class Store : ISupplierRepository, IRiskAnalysisRepository, IOutboxWriter, IOutboxReader, IOutboxDispatcher, IUnitOfWork
    {
        public Supplier Supplier { get; } = new("Supplier", "ML-TEST-001", "TR");
        public TestSerializer Serializer { get; } = new();
        public List<string> Operations { get; } = [];
        public List<IIntegrationMessage> Messages { get; } = [];
        public List<XAIAuditLog> Audits { get; } = [];
        public OutboxMessage? Snapshot { get; set; }
        public Exception? DispatchFailure { get; init; }
        public Exception? CommitFailure { get; set; }
        public bool ExistingAudit { get; set; }
        public bool AuditAppearsOnSecondCheck { get; init; }
        public int AuditChecks { get; private set; }
        public int SnapshotReads { get; private set; }
        public int SupplierLoads { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public Guid DispatchedId { get; private set; }

        public Task BeginTransactionAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("begin");
            return Task.CompletedTask;
        }

        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("commit");
            CommitCount++;
            return CommitFailure is null ? Task.CompletedTask : Task.FromException(CommitFailure);
        }

        public Task RollbackAsync(CancellationToken cancellationToken = default)
        {
            Operations.Add("rollback");
            RollbackCount++;
            return Task.CompletedTask;
        }

        public Task WriteAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
        {
            Operations.Add("stage-request");
            Messages.Add(message);
            return Task.CompletedTask;
        }

        public Task<bool> TryDispatchAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            Operations.Add("dispatch");
            DispatchedId = messageId;
            return DispatchFailure is null ? Task.FromResult(true) : Task.FromException<bool>(DispatchFailure);
        }

        public Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
        {
            SnapshotReads++;
            return Task.FromResult(Snapshot?.Id == messageId ? Snapshot : null);
        }

        public Task<bool> AuditExistsByCorrelationIdAsync(string correlationId, CancellationToken cancellationToken = default)
        {
            AuditChecks++;
            return Task.FromResult(ExistingAudit || (AuditAppearsOnSecondCheck && AuditChecks >= 2));
        }

        public Task<Supplier?> GetSupplierForAnalysisAsync(Guid supplierId, CancellationToken cancellationToken = default)
        {
            SupplierLoads++;
            return GetByIdAsync(supplierId, cancellationToken);
        }

        public Task AddXAIAuditLogAsync(XAIAuditLog auditLog, CancellationToken cancellationToken = default)
        {
            Audits.Add(auditLog);
            return Task.CompletedTask;
        }

        public Task<Supplier?> GetByIdAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Supplier?>(Supplier.Id == supplierId ? Supplier : null);
        public Task<bool> ExistsByBusinessKeyAsync(string countryCode, string taxNumber, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task AddAsync(Supplier supplier, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<IReadOnlyList<Supplier>> GetPageAsync(int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<Supplier>>([Supplier]);
        public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => Task.FromResult(1);
        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Use the unit of work.");
    }

    private sealed class FailingCache(Store store) : IDistributedCache
    {
        public List<string> RemovedKeys { get; } = [];
        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            store.Operations.Add("cache");
            RemovedKeys.Add(key);
            throw new IOException("Redis unavailable");
        }
        public byte[]? Get(string key) => null;
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult<byte[]?>(null);
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
        public void Remove(string key) { }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) { }
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) => Task.CompletedTask;
    }
}
