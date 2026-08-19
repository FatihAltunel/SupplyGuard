using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Events;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Events;
using SupplyGuard.Domain.Services;

namespace SupplyGuard.Application.Features.RiskManagement.EvaluateSupplierRisk;

public sealed class EvaluateSupplierRiskCommandHandler(
    IRiskManagementRepository riskManagementRepository,
    IRiskAssessmentService riskAssessmentService,
    IDomainEventHandler<SupplierRiskAssessedEvent> supplierRiskAssessedEventHandler,
    ICurrentUserService currentUserService,
    TimeProvider timeProvider)
    : ICommandHandler<EvaluateSupplierRiskCommand, EvaluateSupplierRiskResult>
{
    public async Task<Result<EvaluateSupplierRiskResult>> HandleAsync(
        EvaluateSupplierRiskCommand command,
        CancellationToken cancellationToken = default)
    {
        var supplier = await riskManagementRepository.GetSupplierForEvaluationAsync(
            command.SupplierId,
            cancellationToken);
        if (supplier is null)
        {
            return Result<EvaluateSupplierRiskResult>.Failure(
                new Error("Supplier.NotFound", "The supplier was not found."));
        }

        var assessedAtUtc = timeProvider.GetUtcNow();
        RiskAssessmentResult assessmentResult;
        try
        {
            assessmentResult = riskAssessmentService.Evaluate(
                supplier,
                supplier.RiskIndicators.ToArray(),
                assessedAtUtc,
                currentUserService.UserId);
        }
        catch (InvalidOperationException exception)
        {
            return Result<EvaluateSupplierRiskResult>.Failure(
                new Error("RiskAssessment.NotEvaluable", exception.Message));
        }

        supplier.ApplyRiskAssessment(
            assessmentResult,
            command.CorrelationId,
            currentUserService.UserId);

        var assessedEvent = supplier.DomainEvents
            .OfType<SupplierRiskAssessedEvent>()
            .Single(domainEvent => domainEvent.RiskAssessmentId == assessmentResult.Assessment.Id);

        await supplierRiskAssessedEventHandler.HandleAsync(assessedEvent, cancellationToken);
        await riskManagementRepository.SaveChangesAsync(cancellationToken);
        supplier.ClearDomainEvents();

        return Result<EvaluateSupplierRiskResult>.Success(new EvaluateSupplierRiskResult(
            supplier.Id,
            assessmentResult.Assessment.Id,
            assessmentResult.Assessment.OverallRiskScore,
            assessmentResult.Assessment.OverallRiskLevel,
            assessmentResult.EarlyWarning?.Id,
            assessmentResult.Assessment.AssessedAtUtc));
    }
}
