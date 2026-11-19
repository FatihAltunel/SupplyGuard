namespace SupplyGuard.Application.Common.Exceptions;

/// <summary>Only the audit correlation unique constraint may produce this exception.</summary>
public sealed class DuplicateCorrelationException : Exception
{
    public DuplicateCorrelationException(Guid correlationId, Exception? innerException = null)
        : this(correlationId.ToString("D"), innerException) { }

    public DuplicateCorrelationException(string correlationKey, Exception? innerException = null)
        : base("An audit already exists for this risk-analysis correlation.", innerException) => CorrelationKey = correlationKey;

    public string CorrelationKey { get; }
    public Guid CorrelationId => Guid.TryParse(CorrelationKey, out var id) ? id : Guid.Empty;
}
