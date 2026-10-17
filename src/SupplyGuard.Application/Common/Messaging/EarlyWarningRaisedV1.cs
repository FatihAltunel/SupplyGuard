namespace SupplyGuard.Application.Common.Messaging;

public sealed record EarlyWarningRaisedV1(
    Guid MessageId,
    Guid WarningId,
    Guid SupplierId,
    Guid? RiskAssessmentId,
    string Severity,
    string CorrelationId,
    DateTimeOffset OccurredAtUtc) : IIntegrationMessage
{
    public const string TypeName = "early-warning.raised.v1";

    public string MessageType => TypeName;
}
