using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.IntegrationTests;

internal static class RiskAnalysisFixtures
{
    public static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);

    public static RiskAnalysisRequestedEvent Requested(Guid? supplierId = null) => new(
        Guid.Parse("71ddac07-fae2-4729-92f6-064e57ee9b01"), RiskAnalysisRequestedEvent.TypeName,
        Guid.Parse("f30f4f56-45f2-4c8d-92ac-b069dbf48761"), Now,
        supplierId ?? Guid.Parse("e028a42b-10d1-4911-b69b-dfc29986ce12"),
        new(1, Now.AddDays(-1), new Dictionary<string, double?>
        {
            ["averageDeliveryDelayDays"] = 8, ["onTimeDeliveryRate"] = 0.72, ["missing"] = null
        }));

    public static RiskAnalysisCompletedEvent Completed(RiskAnalysisRequestedEvent? requested = null)
    {
        requested ??= Requested();
        return new(Guid.Parse("83f329f9-a83a-4934-8652-0c70041be9a2"), RiskAnalysisCompletedEvent.TypeName,
            requested.CorrelationId, Now.AddSeconds(2), requested.SupplierId, requested.MessageId, "succeeded",
            new("supplyguard-dummy", "0.1.0", true), new(Now, Now.AddSeconds(2), 2000),
            new(65m, 0.9m, new("dummy-shap", "Simulated elevated delivery risk. This is a fixed dummy result.",
                "risk-score-0-100", 30m, [new("averageDeliveryDelayDays", 25m), new("onTimeDeliveryRate", 10m)])), null);
    }
}
