using FluentValidation;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;

namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

public sealed class RequestJitRiskAnalysisCommandHandler(
    ISupplierRepository supplierRepository,
    ISupplierRiskIndicatorReader riskIndicators,
    ISupplierPerformanceService performanceService,
    IFinancialIntegrationService financialService,
    IValidator<SupplierRiskFeatures> featuresValidator,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICommandHandler<RequestJitRiskAnalysisCommand, RequestJitRiskAnalysisResult>
{
    public async Task<Result<RequestJitRiskAnalysisResult>> HandleAsync(
        RequestJitRiskAnalysisCommand command, CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.IsDeleted)
            return Failure("Supplier.NotFound", "The supplier was not found.");

        // Resolve external data before opening the database transaction. Do not silently
        // replace unavailable observations with zero or a fabricated healthy score.
        var trend = await performanceService.GetDeliveryDelayTrendAsync(
            supplier.Id, TimeSpan.FromDays(30), cancellationToken);
        if (trend is null)
            return Failure("RiskAnalysis.TrendUnavailable", "The delivery-delay trend cannot be calculated.");

        var stability = await financialService.GetStabilityScoreAsync(supplier.Id, cancellationToken);
        // Await EF operations sequentially: the scoped DbContext is not thread safe.
        var activeCount = await riskIndicators.CountActiveAsync(supplier.Id, cancellationToken);
        var features = new SupplierRiskFeatures(
            command.AverageDeliveryDelayDays, command.OnTimeDeliveryRate, command.QualityDefectRate,
            trend.Value, stability, activeCount);
        if (!(await featuresValidator.ValidateAsync(features, cancellationToken)).IsValid)
            return Failure("RiskAnalysis.InvalidFeatures", "An integration returned an invalid feature value.");

        var message = new JitRiskAnalysisRequestedEvent(supplier.Id, command.CorrelationId, features)
        {
            MessageId = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow()
        };

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // The existing writer serializes this immutable snapshot and stages an
            // OutboxMessage in the shared DbContext. CommitAsync performs SaveChanges.
            await outboxWriter.WriteAsync(message, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        // Publication is owned by the background outbox dispatcher, after commit.
        return Result<RequestJitRiskAnalysisResult>.Success(new(supplier.Id, message.CorrelationId, message.MessageId));
    }

    private static Result<RequestJitRiskAnalysisResult> Failure(string code, string message) =>
        Result<RequestJitRiskAnalysisResult>.Failure(new Error(code, message));
}
