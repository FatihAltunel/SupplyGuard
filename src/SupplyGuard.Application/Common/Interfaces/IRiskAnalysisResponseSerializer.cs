using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IRiskAnalysisResponseSerializer
{
    string Serialize(RiskAnalysisResponse response);
    RiskAnalysisResponse Deserialize(string payload);
}
