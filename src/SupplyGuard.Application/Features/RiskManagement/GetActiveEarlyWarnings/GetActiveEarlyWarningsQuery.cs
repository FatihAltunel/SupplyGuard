using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.GetActiveEarlyWarnings;

public sealed record GetActiveEarlyWarningsQuery(Guid? SupplierId = null)
    : IQuery<IReadOnlyList<ActiveEarlyWarningDto>>;

public sealed record ActiveEarlyWarningDto(
    Guid Id,
    Guid SupplierId,
    string SupplierName,
    Guid? RiskAssessmentId,
    string Title,
    string Message,
    WarningSeverity Severity,
    WarningStatus Status,
    DateTimeOffset DetectedAtUtc,
    DateTimeOffset? AcknowledgedAtUtc,
    Guid? AcknowledgedByUserId);
