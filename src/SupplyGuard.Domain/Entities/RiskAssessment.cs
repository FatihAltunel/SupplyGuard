using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Entities;

public class RiskAssessment : AuditableEntity
{
    private readonly List<RiskScore> _riskScores = [];

    public Guid SupplierId { get; private set; }
    public decimal OverallRiskScore { get; private set; }
    public RiskLevel OverallRiskLevel { get; private set; }
    public DateTimeOffset AssessedAtUtc { get; private set; }
    public string? Rationale { get; private set; }
    public string? Outcome { get; private set; }
    public Supplier Supplier { get; private set; } = null!;
    public IReadOnlyCollection<RiskScore> RiskScores => _riskScores.AsReadOnly();

    private RiskAssessment()
    {
        // Required by EF Core.
    }

    public RiskAssessment(Guid supplierId, decimal overallRiskScore, RiskLevel overallRiskLevel, DateTimeOffset assessedAtUtc, string? rationale = null, string? outcome = null, Guid? createdByUserId = null)
        : base(createdByUserId)
    {
        SupplierId = RequireId(supplierId, nameof(supplierId));
        OverallRiskScore = RequireScore(overallRiskScore, nameof(overallRiskScore));
        OverallRiskLevel = overallRiskLevel;
        AssessedAtUtc = assessedAtUtc.ToUniversalTime();
        Rationale = NormalizeOptionalText(rationale, 4_000);
        Outcome = NormalizeOptionalText(outcome, 1_000);
    }

    public void AddRiskScore(RiskScore riskScore, Guid? modifiedByUserId = null)
    {
        ArgumentNullException.ThrowIfNull(riskScore);
        if (riskScore.RiskAssessmentId != Id)
        {
            throw new InvalidOperationException("The risk score belongs to a different risk assessment.");
        }

        if (_riskScores.Any(score => score.Category == riskScore.Category))
        {
            throw new InvalidOperationException("Only one risk score per category is permitted in an assessment.");
        }

        _riskScores.Add(riskScore);
        RecalculateOverallRisk();
        MarkAsModified(modifiedByUserId);
    }

    public void UpdateNarrative(string? rationale, string? outcome, Guid? modifiedByUserId = null)
    {
        Rationale = NormalizeOptionalText(rationale, 4_000);
        Outcome = NormalizeOptionalText(outcome, 1_000);
        MarkAsModified(modifiedByUserId);
    }

    private void RecalculateOverallRisk()
    {
        var totalWeight = _riskScores.Sum(score => score.Weight);
        if (totalWeight == 0)
        {
            return;
        }

        OverallRiskScore = Math.Round(_riskScores.Sum(score => score.Score * score.Weight) / totalWeight, 2, MidpointRounding.AwayFromZero);
        OverallRiskLevel = OverallRiskScore switch
        {
            < 25 => RiskLevel.Low,
            < 50 => RiskLevel.Medium,
            < 75 => RiskLevel.High,
            _ => RiskLevel.Critical
        };
        AssessedAtUtc = _riskScores.Max(score => score.CalculatedAtUtc);
    }

    private static Guid RequireId(Guid value, string parameterName) => value == Guid.Empty ? throw new ArgumentException("Identifier cannot be empty.", parameterName) : value;
    private static decimal RequireScore(decimal value, string parameterName) => value is < 0 or > 100 ? throw new ArgumentOutOfRangeException(parameterName, "Score must be between 0 and 100.") : value;
    private static string? NormalizeOptionalText(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximumLength ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maximumLength} characters.");
}
