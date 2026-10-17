using SupplyGuard.Application.Common.Events;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Features.RiskManagement.AcknowledgeEarlyWarning;
using SupplyGuard.Application.Features.RiskManagement.EvaluateSupplierRisk;
using SupplyGuard.Application.Features.RiskManagement.Events;
using SupplyGuard.Application.Features.RiskManagement.GetSupplierCurrentRisk;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Events;
using SupplyGuard.Domain.Services;

namespace SupplyGuard.Application.Tests;

public class RiskManagementUseCaseTests
{
    private static readonly DateTimeOffset AssessedAtUtc =
        new(2026, 8, 19, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EvaluateSupplierRisk_StagesWarningMessageBeforeSavingOnce()
    {
        var supplier = CreateSupplierWithIndicator(90m);
        var repository = new FakeRiskManagementRepository(supplier);
        var outboxWriter = new FakeOutboxWriter();
        repository.OnSaving = () =>
        {
            Assert.Single(outboxWriter.Messages);
            Assert.Single(repository.AuditLogs);
            Assert.Single(supplier.EarlyWarnings);
            Assert.NotEmpty(supplier.DomainEvents);
        };
        IDomainEventHandler<SupplierRiskAssessedEvent> eventHandler =
            new SupplierRiskAssessedEventHandler(repository);
        var handler = new EvaluateSupplierRiskCommandHandler(
            repository,
            new RiskAssessmentService(),
            eventHandler,
            new EarlyWarningTriggeredEventHandler(outboxWriter),
            new FakeCurrentUserService(Guid.NewGuid()),
            new FixedTimeProvider(AssessedAtUtc));

        var result = await handler.HandleAsync(new EvaluateSupplierRiskCommand(
            supplier.Id,
            "risk-assessment-001"));

        Assert.True(result.IsSuccess);
        Assert.Equal(90m, result.Value!.OverallRiskScore);
        Assert.NotNull(result.Value.EarlyWarningId);
        Assert.Single(supplier.RiskAssessments);
        Assert.Single(supplier.EarlyWarnings);
        var auditLog = Assert.Single(repository.AuditLogs);
        Assert.Equal(ExplanationStatus.Pending, auditLog.ExplanationStatus);
        Assert.Equal(result.Value.RiskAssessmentId, auditLog.RiskAssessmentId);
        Assert.Equal("risk-assessment-001", auditLog.CorrelationId);
        var message = Assert.IsType<EarlyWarningRaisedV1>(Assert.Single(outboxWriter.Messages));
        Assert.NotEqual(Guid.Empty, message.MessageId);
        Assert.Equal(result.Value.EarlyWarningId, message.WarningId);
        Assert.Equal(supplier.Id, message.SupplierId);
        Assert.Equal(result.Value.RiskAssessmentId, message.RiskAssessmentId);
        Assert.Equal("Critical", message.Severity);
        Assert.Equal("risk-assessment-001", message.CorrelationId);
        Assert.Equal(AssessedAtUtc, message.OccurredAtUtc);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Empty(supplier.DomainEvents);
    }

    [Fact]
    public async Task EvaluateSupplierRisk_WithoutWarning_DoesNotStageNotification()
    {
        var supplier = CreateSupplierWithIndicator(65m);
        var repository = new FakeRiskManagementRepository(supplier);
        var outboxWriter = new FakeOutboxWriter();
        var handler = new EvaluateSupplierRiskCommandHandler(
            repository,
            new RiskAssessmentService(),
            new SupplierRiskAssessedEventHandler(repository),
            new EarlyWarningTriggeredEventHandler(outboxWriter),
            new FakeCurrentUserService(Guid.NewGuid()),
            new FixedTimeProvider(AssessedAtUtc));

        var result = await handler.HandleAsync(new EvaluateSupplierRiskCommand(supplier.Id, "no-warning"));

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value!.EarlyWarningId);
        Assert.Empty(outboxWriter.Messages);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Empty(supplier.DomainEvents);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvaluateSupplierRisk_StagingOrSaveFailure_DoesNotClearEvents(bool failStaging)
    {
        var supplier = CreateSupplierWithIndicator(90m);
        var failure = new InvalidOperationException("Simulated persistence failure");
        var repository = new FakeRiskManagementRepository(supplier)
        {
            SaveFailure = failStaging ? null : failure
        };
        var outboxWriter = new FakeOutboxWriter { Failure = failStaging ? failure : null };
        var handler = new EvaluateSupplierRiskCommandHandler(
            repository,
            new RiskAssessmentService(),
            new SupplierRiskAssessedEventHandler(repository),
            new EarlyWarningTriggeredEventHandler(outboxWriter),
            new FakeCurrentUserService(Guid.NewGuid()),
            new FixedTimeProvider(AssessedAtUtc));

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            handler.HandleAsync(new EvaluateSupplierRiskCommand(supplier.Id, "failed-warning")));

        Assert.Same(failure, thrown);
        Assert.Equal(failStaging ? 0 : 1, repository.SaveChangesCount);
        Assert.Contains(supplier.DomainEvents, domainEvent => domainEvent is EarlyWarningTriggeredEvent);
    }

    [Fact]
    public async Task AcknowledgeEarlyWarning_UsesAuthenticatedUserAndPersistsTransition()
    {
        var supplier = CreateSupplierWithIndicator(90m);
        var assessmentResult = new RiskAssessmentService().Evaluate(
            supplier,
            supplier.RiskIndicators.ToArray(),
            AssessedAtUtc);
        supplier.ApplyRiskAssessment(assessmentResult, "risk-assessment-002");
        supplier.ClearDomainEvents();
        var warning = Assert.Single(supplier.EarlyWarnings);
        var userId = Guid.NewGuid();
        var repository = new FakeRiskManagementRepository(supplier);
        var handler = new AcknowledgeEarlyWarningCommandHandler(
            repository,
            new FakeCurrentUserService(userId),
            new FixedTimeProvider(AssessedAtUtc.AddMinutes(5)));

        var result = await handler.HandleAsync(new AcknowledgeEarlyWarningCommand(
            supplier.Id,
            warning.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(WarningStatus.Acknowledged, warning.Status);
        Assert.Equal(userId, warning.AcknowledgedByUserId);
        Assert.Equal(1, repository.SaveChangesCount);
        Assert.Empty(supplier.DomainEvents);
    }

    [Fact]
    public async Task GetSupplierCurrentRisk_MapsLatestAssessmentAndCategoryScores()
    {
        var supplier = CreateSupplierWithIndicator(65m);
        var assessmentResult = new RiskAssessmentService().Evaluate(
            supplier,
            supplier.RiskIndicators.ToArray(),
            AssessedAtUtc);
        supplier.ApplyRiskAssessment(assessmentResult, "risk-assessment-003");
        var repository = new FakeRiskManagementRepository(supplier)
        {
            CurrentAssessment = assessmentResult.Assessment
        };
        var handler = new GetSupplierCurrentRiskQueryHandler(repository);

        var result = await handler.HandleAsync(new GetSupplierCurrentRiskQuery(supplier.Id));

        Assert.NotNull(result);
        Assert.Equal(65m, result!.OverallRiskScore);
        Assert.Equal(RiskLevel.High, result.OverallRiskLevel);
        Assert.Single(result.Scores);
        Assert.Equal(RiskCategory.Financial, result.Scores[0].Category);
    }

    private static Supplier CreateSupplierWithIndicator(decimal score)
    {
        var supplier = new Supplier("Risk Supplier", Guid.NewGuid().ToString("N"), "TR");
        supplier.AddRiskIndicator(new RiskIndicator(
            supplier.Id,
            RiskCategory.Financial,
            "FIN-01",
            "Financial exposure",
            score >= 75m ? RiskLevel.Critical : RiskLevel.High,
            score,
            score,
            1m,
            "ApplicationTest",
            AssessedAtUtc));
        return supplier;
    }

    private sealed class FakeRiskManagementRepository(Supplier supplier) : IRiskManagementRepository
    {
        public List<XAIAuditLog> AuditLogs { get; } = [];
        public RiskAssessment? CurrentAssessment { get; init; }
        public int SaveChangesCount { get; private set; }
        public Action? OnSaving { get; set; }
        public Exception? SaveFailure { get; init; }

        public Task<Supplier?> GetSupplierForEvaluationAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Supplier?>(supplier.Id == supplierId ? supplier : null);

        public Task<Supplier?> GetSupplierWithWarningsAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<Supplier?>(supplier.Id == supplierId ? supplier : null);

        public Task<RiskAssessment?> GetCurrentRiskAssessmentAsync(Guid supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult(supplier.Id == supplierId ? CurrentAssessment : null);

        public Task<IReadOnlyList<EarlyWarning>> GetActiveEarlyWarningsAsync(Guid? supplierId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EarlyWarning>>(supplier.EarlyWarnings
                .Where(warning => warning.Status is WarningStatus.Open or WarningStatus.Acknowledged)
                .ToArray());

        public Task AddXAIAuditLogAsync(XAIAuditLog xaiAuditLog, CancellationToken cancellationToken = default)
        {
            AuditLogs.Add(xaiAuditLog);
            return Task.CompletedTask;
        }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveChangesCount++;
            OnSaving?.Invoke();
            if (SaveFailure is not null)
            {
                return Task.FromException<int>(SaveFailure);
            }

            return Task.FromResult(1);
        }
    }

    private sealed class FakeOutboxWriter : IOutboxWriter
    {
        public List<IIntegrationMessage> Messages { get; } = [];
        public Exception? Failure { get; init; }

        public Task WriteAsync(IIntegrationMessage message, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeCurrentUserService(Guid? userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public bool IsAuthenticated => UserId.HasValue;
        public string? UserName => "domain-tester";
        public IReadOnlyCollection<string> Roles => [];
        public bool HasPermission(string permission) => true;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
