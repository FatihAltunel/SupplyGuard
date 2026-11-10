namespace SupplyGuard.Application.Common.Interfaces;

public interface ISupplierRiskIndicatorReader
{
    Task<int> CountActiveAsync(Guid supplierId, CancellationToken cancellationToken = default);
}
