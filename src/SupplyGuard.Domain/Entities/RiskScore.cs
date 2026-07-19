using SupplyGuard.Domain.Common;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Entities;

public class RiskScore : BaseEntity
{
    public Guid RiskAssessmentId { get; private set; }
    public RiskCategory Category { get; private set; }
    public decimal Score { get; private set; }
    public decimal Weight { get; private set; }
    public RiskLevel RiskLevel { get; private set; }
    public string? Explanation { get; private set; }
    public DateTimeOffset CalculatedAtUtc { get; private set; }
    public RiskAssessment RiskAssessment { get; private set; } = null!;

    private RiskScore()
    {
        // Required by EF Core.
    }

    public RiskScore(Guid riskAssessmentId, RiskCategory category, decimal score, decimal weight, RiskLevel riskLevel, DateTimeOffset calculatedAtUtc, string? explanation = null)
        : base(Guid.NewGuid())
    {
        RiskAssessmentId = RequireId(riskAssessmentId, nameof(riskAssessmentId));
        Category = category;
        Score = RequireScore(score, nameof(score));
        Weight = RequireWeight(weight);
        RiskLevel = riskLevel;
        CalculatedAtUtc = calculatedAtUtc.ToUniversalTime();
        Explanation = NormalizeOptionalText(explanation, 2_000);
    }

    public void Update(decimal score, decimal weight, RiskLevel riskLevel, string? explanation, DateTimeOffset calculatedAtUtc)
    {
        Score = RequireScore(score, nameof(score));
        Weight = RequireWeight(weight);
        RiskLevel = riskLevel;
        Explanation = NormalizeOptionalText(explanation, 2_000);
        CalculatedAtUtc = calculatedAtUtc.ToUniversalTime();
    }

    private static Guid RequireId(Guid value, string parameterName) => value == Guid.Empty ? throw new ArgumentException("Identifier cannot be empty.", parameterName) : value;
    private static decimal RequireScore(decimal value, string parameterName) => value is < 0 or > 100 ? throw new ArgumentOutOfRangeException(parameterName, "Score must be between 0 and 100.") : value;
    private static decimal RequireWeight(decimal value) => value is <= 0 or > 1 ? throw new ArgumentOutOfRangeException(nameof(value), "Weight must be greater than 0 and no greater than 1.") : value;
    private static string? NormalizeOptionalText(string? value, int maximumLength) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length <= maximumLength ? value.Trim() : throw new ArgumentOutOfRangeException(nameof(value), $"Value cannot exceed {maximumLength} characters.");
}
