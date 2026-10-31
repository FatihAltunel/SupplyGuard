using System.Text.Json;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Application.Tests;

public sealed class RiskAnalysisContractTests
{
    internal const string RequestedJson = """
        {
          "messageId": "71ddac07-fae2-4729-92f6-064e57ee9b01",
          "messageType": "risk-analysis.requested.v1",
          "correlationId": "f30f4f56-45f2-4c8d-92ac-b069dbf48761",
          "occurredAtUtc": "2026-10-27T09:00:00.000Z",
          "supplierId": "e028a42b-10d1-4911-b69b-dfc29986ce12",
          "input": {
            "featureSchemaVersion": 1,
            "observedAtUtc": "2026-10-26T18:00:00.000Z",
            "features": { "averageDeliveryDelayDays": 8.0, "onTimeDeliveryRate": 0.72, "missing": null }
          }
        }
        """;

    internal const string CompletedJson = """
        {
          "messageId": "83f329f9-a83a-4934-8652-0c70041be9a2",
          "messageType": "risk-analysis.completed.v1",
          "correlationId": "f30f4f56-45f2-4c8d-92ac-b069dbf48761",
          "occurredAtUtc": "2026-10-27T09:00:02.000Z",
          "supplierId": "e028a42b-10d1-4911-b69b-dfc29986ce12",
          "requestMessageId": "71ddac07-fae2-4729-92f6-064e57ee9b01",
          "status": "succeeded",
          "model": { "name": "supplyguard-dummy", "version": "0.1.0", "isMock": true },
          "execution": {
            "startedAtUtc": "2026-10-27T09:00:00.000Z",
            "completedAtUtc": "2026-10-27T09:00:02.000Z",
            "latencyMs": 2000
          },
          "result": {
            "riskScore": 65.0,
            "confidenceScore": 0.9,
            "explanation": {
              "method": "dummy-shap",
              "reason": "Simulated elevated delivery risk. This is a fixed dummy result.",
              "outputScale": "risk-score-0-100",
              "baseValue": 30.0,
              "contributions": [
                { "feature": "averageDeliveryDelayDays", "contribution": 25.0 },
                { "feature": "onTimeDeliveryRate", "contribution": 10.0 }
              ]
            }
          },
          "error": null
        }
        """;

    internal static RiskAnalysisRequestedEvent Requested() => JsonSerializer.Deserialize<RiskAnalysisRequestedEvent>(RequestedJson)!;
    internal static RiskAnalysisCompletedEvent Completed() => JsonSerializer.Deserialize<RiskAnalysisCompletedEvent>(CompletedJson)!;

    [Fact]
    public void ApprovedFixtures_MapWithoutFlatteningOrLeakingInterfaceCorrelationProperty()
    {
        var requested = Requested();
        var completed = Completed();
        Assert.Equal(requested.CorrelationId.ToString("D"), ((IIntegrationMessage)requested).CorrelationId);
        Assert.Equal(65m, completed.Result!.RiskScore);
        Assert.Equal(0.9m, completed.Result.ConfidenceScore);
        Assert.Null(requested.Input.Features["missing"]);
        using var payload = JsonDocument.Parse(JsonSerializer.Serialize(completed));
        Assert.Equal(JsonValueKind.Number, payload.RootElement.GetProperty("result").GetProperty("riskScore").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.RootElement.GetProperty("error").ValueKind);
        using var approved = JsonDocument.Parse(CompletedJson);
        Assert.Equal(
            approved.RootElement.EnumerateObject().Select(property => property.Name).Order(),
            payload.RootElement.EnumerateObject().Select(property => property.Name).Order());
        Assert.False(payload.RootElement.TryGetProperty("CorrelationId", out _));
    }

    [Theory]
    [InlineData("error")]
    [InlineData("status")]
    [InlineData("supplierId")]
    public void RequiredWireMembers_CannotBeOmittedEvenWhenNullable(string member)
    {
        var payload = System.Text.Json.Nodes.JsonNode.Parse(CompletedJson)!.AsObject();
        payload.Remove(member);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RiskAnalysisCompletedEvent>(payload.ToJsonString()));
    }

    [Fact]
    public void WrongCaseUnknownPropertiesAndQuotedNumbers_AreRejected()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RiskAnalysisCompletedEvent>(
            CompletedJson.Replace("\"supplierId\"", "\"SupplierId\"")));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RiskAnalysisCompletedEvent>(
            CompletedJson.Replace("\"riskScore\": 65.0", "\"riskScore\": \"65\"")));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<RiskAnalysisRequestedEvent>(
            RequestedJson.Replace("\"featureSchemaVersion\": 1", "\"featureSchemaVersion\": true")));
    }

    [Fact]
    public void CompletionValidator_RejectsInvalidNestedPayloadsWithoutThrowing()
    {
        var valid = Completed();
        var validator = new RiskAnalysisCompletedEventValidator();
        Assert.True(validator.Validate(valid).IsValid);
        var invalid = new[]
        {
            valid with { MessageId = Guid.Empty },
            valid with { OccurredAtUtc = valid.OccurredAtUtc.ToOffset(TimeSpan.FromHours(3)) },
            valid with { Model = null! },
            valid with { Execution = null! },
            valid with { Execution = valid.Execution with { LatencyMs = -1 } },
            valid with { Execution = valid.Execution with { StartedAtUtc = valid.OccurredAtUtc.AddMinutes(1) } },
            valid with { Result = null },
            valid with { Result = valid.Result! with { RiskScore = 65.001m } },
            valid with { Result = valid.Result! with { ConfidenceScore = 0.90001m } },
            valid with { Result = valid.Result! with { Explanation = null! } },
            valid with { Result = valid.Result! with { Explanation = valid.Result!.Explanation with { Contributions = null! } } },
            valid with { Result = valid.Result! with { Explanation = valid.Result!.Explanation with { Contributions = [null!] } } },
            valid with { Result = valid.Result! with { Explanation = valid.Result!.Explanation with { BaseValue = 0 } } },
            valid with { Result = valid.Result! with { Explanation = valid.Result!.Explanation with { BaseValue = decimal.MaxValue } } },
            valid with { Error = new("ERROR", "Unexpected error") },
            valid with { Status = "Succeeded" }
        };
        Assert.All(invalid, message => Assert.False(validator.Validate(message).IsValid));
    }

    [Fact]
    public void InputValidator_RejectsNullCollectionsAndNonfiniteValues()
    {
        var validator = new RiskAnalysisInputV1Validator();
        var input = Requested().Input;
        Assert.False(validator.Validate(input with { Features = null! }).IsValid);
        Assert.False(validator.Validate(input with { Features = new Dictionary<string, double?>() }).IsValid);
        Assert.False(validator.Validate(input with { Features = new Dictionary<string, double?> { ["bad"] = double.NaN } }).IsValid);
        Assert.False(validator.Validate(input with { ObservedAtUtc = input.ObservedAtUtc.AddTicks(1) }).IsValid);
    }
}
