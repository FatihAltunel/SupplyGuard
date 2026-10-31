using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Common.Interfaces;

/// <summary>
/// Serializes concrete message contracts consistently for the broker, outbox, and audit.
/// Risk-analysis v1 requires UTF-8 JSON, exact camelCase names, strict numeric tokens,
/// explicit nulls, canonical non-empty UUIDs, and UTC timestamps with exactly milliseconds
/// and Z. Deserialization rejects missing/unknown members and noncanonical wire values.
/// Malformed payloads throw System.Text.Json.JsonException. Preserve existing message formats.
/// </summary>
public interface IIntegrationMessageSerializer
{
    string Serialize(IIntegrationMessage message);
    TMessage Deserialize<TMessage>(string payload) where TMessage : class, IIntegrationMessage;
}
