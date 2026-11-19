using System.Text.Json;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

namespace SupplyGuard.Infrastructure.Messaging.Serialization;

public sealed class RiskAnalysisResponseSerializer : IRiskAnalysisResponseSerializer
{
    public const int MaximumPayloadLength = 100_000;
    private readonly RiskAnalysisResponseValidator _validator = new();

    public string Serialize(RiskAnalysisResponse response)
    {
        Validate(response);
        var payload = JsonSerializer.Serialize(response, IntegrationMessageSerializer.StrictOptions);
        CheckLength(payload);
        return payload;
    }

    public RiskAnalysisResponse Deserialize(string payload)
    {
        CheckLength(payload);
        using var document = JsonDocument.Parse(payload);
        IntegrationMessageSerializer.RejectDuplicateMembers(document.RootElement);
        var response = JsonSerializer.Deserialize<RiskAnalysisResponse>(payload, IntegrationMessageSerializer.StrictOptions)
            ?? throw new JsonException("Risk-analysis response cannot be null.");
        Validate(response);
        return response;
    }

    private void Validate(RiskAnalysisResponse response)
    {
        if (response is null || !_validator.Validate(response).IsValid)
            throw new JsonException("Invalid risk-analysis response.");
    }

    private static void CheckLength(string payload)
    {
        if (payload.Length > MaximumPayloadLength) throw new JsonException("Risk-analysis response exceeds the audit limit.");
    }
}
