using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Domain.Services;

public static class RiskLevelPolicy
{
    public static RiskLevel FromScore(decimal score)
    {
        if (score is < 0m or > 100m)
        {
            throw new ArgumentOutOfRangeException(nameof(score), "Risk score must be between 0 and 100.");
        }

        return score switch
        {
            < 25m => RiskLevel.Low,
            < 50m => RiskLevel.Medium,
            < 75m => RiskLevel.High,
            _ => RiskLevel.Critical
        };
    }
}
