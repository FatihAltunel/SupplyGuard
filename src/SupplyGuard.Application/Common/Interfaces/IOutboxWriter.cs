using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IOutboxWriter
{
    /// <summary>
    /// Stages a message in the current unit of work without saving or publishing it.
    /// The caller commits it together with the business changes.
    /// </summary>
    Task WriteAsync(
        IIntegrationMessage message,
        CancellationToken cancellationToken = default);
}
