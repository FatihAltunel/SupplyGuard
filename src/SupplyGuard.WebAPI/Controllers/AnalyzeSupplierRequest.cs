using System.Text.Json.Serialization;
using SupplyGuard.Infrastructure.Messaging.Serialization;

namespace SupplyGuard.WebAPI.Controllers;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed record AnalyzeSupplierRequest(
    [property: JsonRequired, JsonPropertyName("featureSchemaVersion")] int FeatureSchemaVersion,
    [property: JsonRequired, JsonPropertyName("observedAtUtc"), JsonConverter(typeof(UtcMillisecondsJsonConverter))] DateTimeOffset ObservedAtUtc,
    [property: JsonRequired, JsonPropertyName("features")] IReadOnlyDictionary<string, double?> Features);
