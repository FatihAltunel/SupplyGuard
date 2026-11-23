using Microsoft.EntityFrameworkCore;
using SupplyGuard.Domain.Entities;

namespace SupplyGuard.Infrastructure.Persistence.Seeding;

/// <summary>
/// Adds fictional demo suppliers so the demo UI has something to analyze.
/// Run only together with simulated integrations (Development). Existing rows, including
/// soft-deleted ones with the same business key, are never modified.
/// </summary>
public sealed class DemoSupplierSeeder(SupplyGuardDbContext context)
{
    private static readonly (string Name, string TaxNumber, string City, string Category, bool Critical)[] Suppliers =
    [
        ("Demo Anadolu Döküm A.Ş.", "DEMO0000000001", "Bursa", "Döküm parçaları", true),
        ("Demo Marmara Elektronik Ltd.", "DEMO0000000002", "Kocaeli", "Elektronik modüller", true),
        ("Demo Ege Plastik San.", "DEMO0000000003", "İzmir", "Plastik enjeksiyon", false)
    ];

    public async Task<int> SeedAsync(CancellationToken cancellationToken = default)
    {
        var taxNumbers = Suppliers.Select(supplier => supplier.TaxNumber).ToArray();
        var existing = await context.Suppliers.IgnoreQueryFilters()
            .Where(supplier => supplier.CountryCode == "TR" && taxNumbers.Contains(supplier.TaxNumber))
            .Select(supplier => supplier.TaxNumber)
            .ToListAsync(cancellationToken);

        var added = 0;
        foreach (var demo in Suppliers.Where(demo => !existing.Contains(demo.TaxNumber)))
        {
            var supplier = new Supplier(demo.Name, demo.TaxNumber, "TR");
            supplier.UpdateProfile(null, demo.City, null, "Otomotiv JIT", demo.Category);
            supplier.SetCriticality(demo.Critical);
            context.Suppliers.Add(supplier);
            added++;
        }

        if (added > 0) await context.SaveChangesAsync(cancellationToken);
        return added;
    }
}
