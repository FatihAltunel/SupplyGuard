using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Infrastructure.Messaging.Serialization;

public sealed class UtcMillisecondsJsonConverter : JsonConverter<DateTimeOffset>
{
    private const string Format = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a UTC timestamp string.");
        var text = reader.GetString();
        if (text?.Length != 24 || !DateTimeOffset.TryParseExact(text, Format, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value) ||
            text != value.ToString(Format, CultureInfo.InvariantCulture))
            throw new JsonException("Expected UTC ISO 8601 with exactly milliseconds and Z.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options)
    {
        if (!RiskAnalysisContractRules.IsUtcMilliseconds(value))
            throw new JsonException("Timestamp must be UTC with millisecond precision.");
        writer.WriteStringValue(value.ToString(Format, CultureInfo.InvariantCulture));
    }
}
