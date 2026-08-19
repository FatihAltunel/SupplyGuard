using System.Text.Json;
using SupplyGuard.Application.Common.Events;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;
using SupplyGuard.Domain.Events;

namespace SupplyGuard.Application.Features.RiskManagement.Events;

public sealed class SupplierRiskAssessedEventHandler(IRiskManagementRepository riskManagementRepository)
    : IDomainEventHandler<SupplierRiskAssessedEvent>
{
    public Task HandleAsync(
        SupplierRiskAssessedEvent domainEvent,
        CancellationToken cancellationToken = default)
    {
        var assessmentSnapshot = JsonSerializer.Serialize(new
        {
            domainEvent.SupplierId,
            domainEvent.RiskAssessmentId,
            domainEvent.OverallRiskScore,
            OverallRiskLevel = domainEvent.OverallRiskLevel.ToString(),
            domainEvent.OccurredAtUtc
        });

        var auditLog = new XAIAuditLog(
            domainEvent.SupplierId,
            "Unassigned",
            "Unassigned",
            domainEvent.CorrelationId,
            assessmentSnapshot,
            responsePayload: null,
            confidenceScore: 0m,
            latencyMs: 0,
            isSuccessful: false,
            executedAtUtc: domainEvent.OccurredAtUtc,
            riskAssessmentId: domainEvent.RiskAssessmentId,
            explanationStatus: ExplanationStatus.Pending);

        return riskManagementRepository.AddXAIAuditLogAsync(auditLog, cancellationToken);
    }
}
