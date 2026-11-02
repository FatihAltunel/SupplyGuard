using System.Text.Json;
using System.Text.Json.Nodes;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Infrastructure.Messaging.Serialization;
using SupplyGuard.WebAPI.Controllers;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class IntegrationMessageSerializerTests
{
    private readonly IntegrationMessageSerializer _serializer = new();

    [Fact]
    public void Contracts_RoundTripWithCanonicalDatesIdsNumbersAndExplicitNulls()
    {
        var requested = RiskAnalysisFixtures.Requested();
        var completed = RiskAnalysisFixtures.Completed(requested);
        var requestJson = _serializer.Serialize(requested);
        var resultJson = _serializer.Serialize(completed);
        Assert.Contains("2026-11-02T09:00:00.000Z", requestJson);
        Assert.Contains("2026-11-02T09:00:02.000Z", resultJson);
        Assert.DoesNotContain("+00:00", resultJson);
        Assert.Equal(requested.CorrelationId, _serializer.Deserialize<RiskAnalysisRequestedEvent>(requestJson).CorrelationId);
        Assert.Equal(65m, _serializer.Deserialize<RiskAnalysisCompletedEvent>(resultJson).Result!.RiskScore);
        using var document = JsonDocument.Parse(resultJson);
        Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("result").GetProperty("confidenceScore").ValueKind);
        Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty("error").ValueKind);
    }

    [Theory]
    [InlineData("71DDAC07-FAE2-4729-92F6-064E57EE9B01")]
    [InlineData("71ddac07fae2472992f6064e57ee9b01")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public void NoncanonicalIds_AreRejected(string id)
    {
        var payload = JsonNode.Parse(_serializer.Serialize(RiskAnalysisFixtures.Requested()))!;
        payload["messageId"] = id;
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisRequestedEvent>(payload.ToJsonString()));
    }

    [Theory]
    [InlineData("2026-11-02T09:00:00Z")]
    [InlineData("2026-11-02T09:00:00.000+00:00")]
    [InlineData("2026-11-02T12:00:00.000+03:00")]
    [InlineData("2026-11-02T09:00:00.000000Z")]
    [InlineData("2026-02-30T09:00:00.000Z")]
    public void NoncanonicalDates_AreRejected(string timestamp)
    {
        var payload = JsonNode.Parse(_serializer.Serialize(RiskAnalysisFixtures.Requested()))!;
        payload["occurredAtUtc"] = timestamp;
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisRequestedEvent>(payload.ToJsonString()));
    }

    [Fact]
    public void RequiredMembersDuplicateKeysAndInvalidNumbers_AreRejected()
    {
        var json = _serializer.Serialize(RiskAnalysisFixtures.Completed());
        var payload = JsonNode.Parse(json)!;
        payload.AsObject().Remove("error");
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisCompletedEvent>(payload.ToJsonString()));
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisCompletedEvent>(json.Replace("\"error\":null", "\"error\":null,\"error\":null")));
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisCompletedEvent>(json.Replace("\"status\"", "\"Status\"")));
        payload = JsonNode.Parse(json)!;
        payload["result"]!["riskScore"] = "65";
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisCompletedEvent>(payload.ToJsonString()));
        payload = JsonNode.Parse(json)!;
        payload["result"]!["explanation"]!["baseValue"] = 0;
        Assert.Throws<JsonException>(() => _serializer.Deserialize<RiskAnalysisCompletedEvent>(payload.ToJsonString()));
    }

    [Fact]
    public void TerminalFailure_AndExistingNotificationFormatRemainSupported()
    {
        var failed = RiskAnalysisFixtures.Completed() with { Status = "failed", Result = null, Error = new("DUMMY_PROCESSING_FAILED", "Dummy failed.") };
        Assert.Equal(failed, _serializer.Deserialize<RiskAnalysisCompletedEvent>(_serializer.Serialize(failed)));
        var legacy = new EarlyWarningRaisedV1(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "Critical", "legacy-correlation", DateTimeOffset.UtcNow);
        Assert.Equal(JsonSerializer.Serialize(legacy), _serializer.Serialize(legacy));
    }

    [Fact]
    public void HttpRequest_UsesStrictTimestampAndNumberHandlingEvenWithWebDefaults()
    {
        const string json = """{"featureSchemaVersion":1,"observedAtUtc":"2026-11-02T09:00:00.000Z","features":{"delay":8}}""";
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.NotNull(JsonSerializer.Deserialize<AnalyzeSupplierRequest>(json, options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalyzeSupplierRequest>(json.Replace(":8", ":\"8\""), options));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<AnalyzeSupplierRequest>(json.Replace(".000Z", "Z"), options));
    }
}
