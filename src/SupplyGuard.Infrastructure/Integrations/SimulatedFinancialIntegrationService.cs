using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Infrastructure.Integrations;

/// <summary>Demo fixture only; replace with a real integration before using actual supplier data.</summary>
public sealed class SimulatedFinancialIntegrationService : IFinancialIntegrationService
{
    public Task<decimal> GetStabilityScoreAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier identifier is required.", nameof(supplierId));
        return Task.FromResult(85m);
    }
}
