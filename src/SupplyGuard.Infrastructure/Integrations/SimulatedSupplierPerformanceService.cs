using SupplyGuard.Application.Common.Interfaces;

namespace SupplyGuard.Infrastructure.Integrations;

/// <summary>Demo fixture only. No purchase orders or ERP records are queried.</summary>
public sealed class SimulatedSupplierPerformanceService : ISupplierPerformanceService
{
    public Task<decimal?> GetDeliveryDelayTrendAsync(
        Guid supplierId, TimeSpan timespan, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (supplierId == Guid.Empty) throw new ArgumentException("Supplier identifier is required.", nameof(supplierId));
        if (timespan != TimeSpan.FromDays(30))
            throw new ArgumentOutOfRangeException(nameof(timespan), "The demo fixture covers consecutive 30-day windows.");

        // Synthetic observations, not real supplier measurements. Each window is complete.
        decimal[] previousWindowDelayDays = [1m, 1m];
        decimal[] currentWindowDelayDays = [1.10m, 1.20m];
        return Task.FromResult(CalculateTrend(previousWindowDelayDays, currentWindowDelayDays));
    }

    public static decimal? CalculateTrend(IReadOnlyCollection<decimal> previous, IReadOnlyCollection<decimal> current)
    {
        if (previous.Count == 0 || current.Count == 0) return null;
        if (previous.Any(delay => delay < 0) || current.Any(delay => delay < 0))
            throw new ArgumentOutOfRangeException(nameof(current), "Delay days cannot be negative.");
        var baseline = previous.Average();
        return baseline == 0 ? null : (current.Average() - baseline) / baseline * 100m;
    }
}
