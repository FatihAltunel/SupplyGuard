namespace SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

public static class RiskAnalysisContractRules
{
    // Matches the existing append-only XAIAuditLog payload limits.
    public const int MaximumPayloadLength = 100_000;
    public const string Succeeded = "succeeded";
    public const string Failed = "failed";

    public static bool IsUtcMilliseconds(DateTimeOffset value) =>
        value.Offset == TimeSpan.Zero && value.Ticks % TimeSpan.TicksPerMillisecond == 0;

    internal static bool HasText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    internal static bool ReconstructsScore(RiskAnalysisResultV1 result)
    {
        if (result.Explanation?.Contributions is null || result.Explanation.Contributions.Any(item => item is null))
        {
            return false;
        }

        try
        {
            var total = result.Explanation.BaseValue + result.Explanation.Contributions.Sum(item => item.Contribution);
            return Math.Abs(total - result.RiskScore) <= 0.000001m;
        }
        catch (OverflowException)
        {
            return false;
        }
    }
}
