using SupplyGuard.Application.Common.Exceptions;
using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Infrastructure.Integrations;

/// <summary>Fail explicitly until real adapters are registered; never manufacture production features.</summary>
public sealed class UnavailableSupplierIntegrations : ISupplierPerformanceService, IFinancialIntegrationService
{
    public Task<decimal?> GetDeliveryDelayTrendAsync(Guid supplierId, TimeSpan timespan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new SupplierIntegrationUnavailableException();
    }

    public Task<decimal> GetStabilityScoreAsync(Guid supplierId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        throw new SupplierIntegrationUnavailableException();
    }
}
