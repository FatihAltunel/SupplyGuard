using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SupplyGuard.Application;
using SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Persistence.Seeding;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class DemoSupplierSeederTests
{
    [Fact]
    public async Task Seed_AddsDemoSuppliersOnce_AndKeepsExistingBusinessKeys()
    {
        await using var store = await JitRiskAnalysisTests.Store.CreateAsync();
        await using (var context = store.Context())
        {
            context.Suppliers.Add(new Supplier("Already present", "DEMO0000000002", "TR"));
            await context.SaveChangesAsync();
        }

        await using (var context = store.Context())
            Assert.Equal(2, await new DemoSupplierSeeder(context).SeedAsync());
        await using (var context = store.Context())
            Assert.Equal(0, await new DemoSupplierSeeder(context).SeedAsync());

        await using var check = store.Context();
        var suppliers = await check.Suppliers.ToListAsync();
        Assert.Equal(3, suppliers.Count);
        Assert.Equal("Already present", suppliers.Single(supplier => supplier.TaxNumber == "DEMO0000000002").Name);
    }

    [Fact]
    public void OverridePolicy_IsDisabledByDefault_AndEnabledOnlyWithSimulatedIntegrations()
    {
        var production = new ServiceCollection().AddJitRiskAnalysis().BuildServiceProvider();
        Assert.False(production.GetRequiredService<JitFeatureOverridePolicy>().Enabled);
        Assert.Null(production.GetService<DemoSupplierSeeder>());

        // Order-independent: the demo registration wins whether it runs before or after the JIT registration.
        var demoAfter = new ServiceCollection().AddJitRiskAnalysis().AddSimulatedSupplierIntegrations();
        var demoBefore = new ServiceCollection().AddSimulatedSupplierIntegrations().AddJitRiskAnalysis();
        Assert.True(demoAfter.BuildServiceProvider().GetRequiredService<JitFeatureOverridePolicy>().Enabled);
        Assert.True(demoBefore.BuildServiceProvider().GetRequiredService<JitFeatureOverridePolicy>().Enabled);
    }
}
