using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.RiskManagement.AcknowledgeEarlyWarning;

public sealed record AcknowledgeEarlyWarningCommand(
    Guid SupplierId,
    Guid EarlyWarningId) : ICommand<Guid>;
