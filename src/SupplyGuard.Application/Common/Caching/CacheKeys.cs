namespace SupplyGuard.Application.Common.Caching;

public static class CacheKeys
{
    public static string SupplierDetails(Guid supplierId) => $"supplyguard:supplier:v1:{supplierId:N}";
}
