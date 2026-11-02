using System.Text.Json;
using System.Text.Json.Serialization;

namespace SupplyGuard.Infrastructure.Messaging.Serialization;

public sealed class CanonicalGuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("Expected a canonical UUID string.");
        var text = reader.GetString();
        if (text?.Length != 36 || !Guid.TryParseExact(text, "D", out var value) ||
            value == Guid.Empty || text != value.ToString("D"))
            throw new JsonException("Expected a non-empty lowercase, hyphenated UUID.");
        return value;
    }

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options)
    {
        if (value == Guid.Empty) throw new JsonException("UUID cannot be empty.");
        writer.WriteStringValue(value.ToString("D"));
    }
}
