namespace SupplyGuard.Application.Common.Exceptions;

/// <summary>Only the audit correlation unique constraint may produce this exception.</summary>
public sealed class DuplicateCorrelationException(Guid correlationId, Exception? innerException = null)
    : Exception("An audit already exists for this risk-analysis correlation.", innerException)
{
    public Guid CorrelationId { get; } = correlationId;
}
