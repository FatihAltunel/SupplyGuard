using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Domain.Services;

public sealed record RiskAssessmentResult(
    RiskAssessment Assessment,
    EarlyWarning? EarlyWarning);
