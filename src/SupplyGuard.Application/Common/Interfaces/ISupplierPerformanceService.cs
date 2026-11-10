namespace SupplyGuard.Application.Common.Interfaces;

public interface ISupplierPerformanceService
{
    /// <summary>
    /// Compares mean delay in the latest rolling window with the preceding, equally
    /// sized window: (current - previous) / previous * 100. Positive means worsening.
    /// Returns null when a window has no observations or the previous mean is zero.
    /// </summary>
    Task<decimal?> GetDeliveryDelayTrendAsync(
        Guid supplierId, TimeSpan timespan, CancellationToken cancellationToken = default);
}
