using FluentValidation;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;
using SupplyGuard.Application.Common.Exceptions;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;

public sealed class RequestJitRiskAnalysisCommandHandler(
    ISupplierRepository supplierRepository,
    ISupplierRiskIndicatorReader riskIndicators,
    ISupplierPerformanceService performanceService,
    IFinancialIntegrationService financialService,
    IValidator<SupplierRiskFeatures> featuresValidator,
    IOutboxWriter outboxWriter,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    IXAIAuditLogRepository audits,
    IRiskAnalysisRepository analysisRepository,
    IIntegrationMessageSerializer serializer,
    JitFeatureOverridePolicy? overridePolicy = null) : ICommandHandler<RequestJitRiskAnalysisCommand, RequestJitRiskAnalysisResult>
{
    public async Task<Result<RequestJitRiskAnalysisResult>> HandleAsync(
        RequestJitRiskAnalysisCommand command, CancellationToken cancellationToken = default)
    {
        var overrides = command.Overrides is { IsEmpty: false } value ? value : null;
        if (overrides is not null && overridePolicy?.Enabled != true)
            return Failure("RiskAnalysis.OverridesNotAllowed", "Feature overrides are only accepted in demo mode.");

        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.IsDeleted)
            return Failure("Supplier.NotFound", "The supplier was not found.");

        // Resolve external data before opening the database transaction. Do not silently
        // replace unavailable observations with zero or a fabricated healthy score.
        decimal? trend = overrides?.DeliveryDelayTrendPercentage;
        decimal? stability = overrides?.FinancialStabilityScore;
        try
        {
            trend ??= await performanceService.GetDeliveryDelayTrendAsync(
                supplier.Id, TimeSpan.FromDays(30), cancellationToken);
            if (trend is null)
                return Failure("RiskAnalysis.TrendUnavailable", "The delivery-delay trend cannot be calculated.");
            stability ??= await financialService.GetStabilityScoreAsync(supplier.Id, cancellationToken);
        }
        catch (SupplierIntegrationUnavailableException)
        {
            return Failure("RiskAnalysis.IntegrationsUnavailable", "Supplier performance or financial data is currently unavailable.");
        }
        // Await EF operations sequentially: the scoped DbContext is not thread safe.
        var activeCount = overrides?.ActiveRiskIndicatorsCount
            ?? await riskIndicators.CountActiveAsync(supplier.Id, cancellationToken);
        var features = new SupplierRiskFeatures(
            command.AverageDeliveryDelayDays, command.OnTimeDeliveryRate, command.QualityDefectRate,
            trend.Value, stability.Value, activeCount);
        if (!(await featuresValidator.ValidateAsync(features, cancellationToken)).IsValid)
            return Failure("RiskAnalysis.InvalidFeatures", "An integration returned an invalid feature value.");

        var message = new RiskAnalysisRequest(command.CorrelationId, supplier.Id,
            features.AverageDeliveryDelayDays, features.OnTimeDeliveryRate, features.QualityDefectRate,
            features.DeliveryDelayTrendPercentage, features.FinancialStabilityScore, features.ActiveRiskIndicatorsCount)
        {
            MessageId = Guid.NewGuid(),
            OccurredAtUtc = timeProvider.GetUtcNow()
        };
        var payload = serializer.Serialize(message);

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize competing requests for the same supplier, then recheck global correlation ownership.
            if (await analysisRepository.GetSupplierForAnalysisAsync(supplier.Id, cancellationToken) is null)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return Failure("Supplier.NotFound", "The supplier was not found.");
            }
            var existing = await audits.GetByCorrelationIdAsync(message.CorrelationId, cancellationToken);
            if (existing is not null)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return Existing(existing, message, payload);
            }
            await audits.AddAsync(XAIAuditLog.CreatePending(supplier.Id, message.CorrelationId,
                message.MessageId, payload, message.OccurredAtUtc), cancellationToken);
            // The audit and exact publication snapshot share the same DbContext and commit.
            await outboxWriter.WriteAsync(message, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch (DuplicateCorrelationException exception) when (exception.CorrelationKey == message.CorrelationId)
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            var existing = await audits.GetByCorrelationIdAsync(message.CorrelationId, cancellationToken);
            if (existing is null) throw;
            return Existing(existing, message, payload);
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

    private static Result<RequestJitRiskAnalysisResult> Existing(XAIAuditLog audit, RiskAnalysisRequest request, string payload) =>
        audit.SupplierId == request.SupplierId && audit.RequestPayload == payload && audit.RequestMessageId is { } messageId
            ? Result<RequestJitRiskAnalysisResult>.Success(new(audit.SupplierId, audit.CorrelationId, messageId))
            : Failure("RiskAnalysis.CorrelationConflict", "The correlation already belongs to a different analysis request.");
}
