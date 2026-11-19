using System.Text.Json;
using System.Text.Json.Serialization;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Jit.V1;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.Worker.V1;

namespace SupplyGuard.Infrastructure.Messaging.Serialization;

public sealed class IntegrationMessageSerializer : IIntegrationMessageSerializer
{
    internal static readonly JsonSerializerOptions StrictOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        NumberHandling = JsonNumberHandling.Strict,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        Converters = { new CanonicalGuidJsonConverter(), new UtcMillisecondsJsonConverter() }
    };
    // Stateless validators are owned by this serializer, not captured from scoped DI.
    private readonly RiskAnalysisRequestedEventValidator _requests = new(new RiskAnalysisInputV1Validator());
    private readonly RiskAnalysisCompletedEventValidator _completions = new();
    private readonly JitRiskAnalysisRequestedEventValidator _jitRequests = new();
    private readonly RiskAnalysisRequestValidator _workerRequests = new();

    public string Serialize(IIntegrationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Validate(message);
        if (message is JitRiskAnalysisRequestedEvent or RiskAnalysisRequest &&
            (message.MessageId == Guid.Empty || message.OccurredAtUtc == default || message.OccurredAtUtc.Offset != TimeSpan.Zero))
            throw new JsonException("JIT publication requires persisted delivery metadata.");
        return JsonSerializer.Serialize(message, message.GetType(), IsRiskAnalysis(message.GetType()) ? StrictOptions : null);
    }

    public TMessage Deserialize<TMessage>(string payload) where TMessage : class, IIntegrationMessage
    {
        var type = typeof(TMessage);
        if (IsRiskAnalysis(type))
        {
            using var document = JsonDocument.Parse(payload);
            RejectDuplicateMembers(document.RootElement);
        }
        var message = JsonSerializer.Deserialize<TMessage>(payload, IsRiskAnalysis(type) ? StrictOptions : null)
            ?? throw new JsonException("Integration message cannot be null.");
        Validate(message);
        return message;
    }

    private void Validate(IIntegrationMessage message)
    {
        var valid = message switch
        {
            RiskAnalysisRequestedEvent requested => _requests.Validate(requested).IsValid,
            RiskAnalysisCompletedEvent completed => _completions.Validate(completed).IsValid,
            JitRiskAnalysisRequestedEvent jit => _jitRequests.Validate(jit).IsValid,
            RiskAnalysisRequest worker => _workerRequests.Validate(worker).IsValid,
            _ => true // Preserve the established early-warning format and validation path.
        };
        if (!valid) throw new JsonException("Invalid risk-analysis contract.");
    }

    private static bool IsRiskAnalysis(Type type) =>
        type == typeof(RiskAnalysisRequestedEvent) || type == typeof(RiskAnalysisCompletedEvent) ||
        type == typeof(JitRiskAnalysisRequestedEvent) || type == typeof(RiskAnalysisRequest);

    internal static void RejectDuplicateMembers(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate JSON member.");
                RejectDuplicateMembers(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray()) RejectDuplicateMembers(item);
        }
    }
}
