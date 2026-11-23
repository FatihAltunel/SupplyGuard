using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Models;
using SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.DemoMode;
using SupplyGuard.Infrastructure.Identity.Entities;
using SupplyGuard.Infrastructure.Identity.Seeding;
using SupplyGuard.Infrastructure.Identity.Services;
using SupplyGuard.Infrastructure.Persistence;
using SupplyGuard.Infrastructure.Persistence.Seeding;
using SupplyGuard.WebAPI.Controllers;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class DemoModeTests
{
    private static readonly AuthenticatedUser DemoUser = new(Guid.NewGuid(), DemoModeOptions.DemoUserName, null,
        [Roles.DemoUser], Roles.DemoUserPermissions.ToArray());

    [Fact]
    public async Task DemoLogin_IsNotFound_WhenDemoModeIsDisabled()
    {
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var controller = Controller(enabled: false, DemoUser, tokens.Object);

        var response = await controller.DemoLogin(CancellationToken.None);

        Assert.IsType<NotFoundResult>(response.Result);
    }

    [Fact]
    public async Task DemoLogin_IssuesTokensForTheSeededDemoAccount()
    {
        var expected = new TokenResult("access", DateTimeOffset.UtcNow.AddMinutes(15), "refresh", DateTimeOffset.UtcNow.AddDays(1));
        var tokens = new Mock<ITokenService>();
        tokens.Setup(service => service.CreateTokenPairAsync(DemoUser, It.IsAny<CancellationToken>())).ReturnsAsync(expected);

        var response = await Controller(enabled: true, DemoUser, tokens.Object).DemoLogin(CancellationToken.None);

        Assert.Same(expected, Assert.IsType<OkObjectResult>(response.Result).Value);
    }

    [Fact]
    public async Task DemoLogin_RefusesMissingOrElevatedAccount()
    {
        var tokens = new Mock<ITokenService>(MockBehavior.Strict);
        var elevated = DemoUser with { Roles = [Roles.DemoUser, Roles.Administrator] };

        foreach (var user in new[] { null, elevated })
        {
            var response = await Controller(enabled: true, user, tokens.Object).DemoLogin(CancellationToken.None);
            Assert.Equal(StatusCodes.Status503ServiceUnavailable, Assert.IsType<StatusCodeResult>(response.Result).StatusCode);
        }
    }

    [Fact]
    public async Task DemoUserSeeder_CreatesPasswordlessLimitedAccount_AndRevertsManualElevation()
    {
        await using var identity = await IdentityHarness.CreateAsync();

        await identity.SeedAsync();
        var seeded = await identity.FindDemoUserAsync();
        Assert.Equal([Roles.DemoUser], seeded.Roles);
        Assert.Equal(Roles.DemoUserPermissions.Order(), seeded.Permissions.Order());

        // Someone elevates the shared account by hand; the next startup seeding must undo it.
        await using (var scope = identity.Services.CreateAsyncScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
            await roles.CreateAsync(new AppRole { Name = Roles.Administrator });
            var user = (await users.FindByNameAsync(DemoModeOptions.DemoUserName))!;
            Assert.False(await users.HasPasswordAsync(user));
            await users.AddPasswordAsync(user, "Elevated-Password-123!");
            await users.AddToRoleAsync(user, Roles.Administrator);
            await users.AddClaimAsync(user, new(Permissions.ClaimType, Permissions.IdentityManage));
            var demoRole = (await roles.FindByNameAsync(Roles.DemoUser))!;
            await roles.AddClaimAsync(demoRole, new(Permissions.ClaimType, Permissions.SuppliersCreate));
        }

        await identity.SeedAsync();
        var reverted = await identity.FindDemoUserAsync();
        Assert.Equal([Roles.DemoUser], reverted.Roles);
        Assert.Equal(Roles.DemoUserPermissions.Order(), reverted.Permissions.Order());
        await using var check = identity.Services.CreateAsyncScope();
        var manager = check.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
        Assert.False(await manager.HasPasswordAsync((await manager.FindByNameAsync(DemoModeOptions.DemoUserName))!));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddDemoMode_RegistersDemoServicesOnlyWhenEnabled(bool enabled)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["DemoMode:Enabled"] = enabled.ToString() })
            .Build();
        var services = new ServiceCollection().AddJitRiskAnalysis().AddDemoMode(configuration);

        Assert.Equal(enabled, services.BuildServiceProvider().GetRequiredService<JitFeatureOverridePolicy>().Enabled);
        Assert.Equal(enabled, services.Any(descriptor => descriptor.ServiceType == typeof(DemoSupplierSeeder)));
        Assert.Equal(enabled, services.Any(descriptor => descriptor.ServiceType == typeof(DemoUserSeeder)));
        Assert.Equal(enabled, services.Any(descriptor => descriptor.ServiceType == typeof(ISupplierPerformanceService)));
    }

    private static AuthController Controller(bool enabled, AuthenticatedUser? user, ITokenService tokens)
    {
        var identity = new Mock<IIdentityService>();
        identity.Setup(service => service.FindUserByNameAsync(DemoModeOptions.DemoUserName, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        return new AuthController(identity.Object, tokens, Options.Create(new DemoModeOptions { Enabled = enabled }));
    }

    /// <summary>Real ASP.NET Core Identity stores on in-memory SQLite.</summary>
    private sealed class IdentityHarness(SqliteConnection connection, ServiceProvider services) : IAsyncDisposable
    {
        public ServiceProvider Services => services;

        public static async Task<IdentityHarness> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddDbContext<IdentityOnlyContext>(options => options.UseSqlite(connection));
            services.AddIdentityCore<AppUser>().AddRoles<AppRole>().AddEntityFrameworkStores<IdentityOnlyContext>();
            services.AddScoped<DemoUserSeeder>();
            services.AddScoped<IdentityService>();
            var provider = services.BuildServiceProvider();
            await using (var scope = provider.CreateAsyncScope())
                await scope.ServiceProvider.GetRequiredService<IdentityOnlyContext>().Database.EnsureCreatedAsync();
            return new(connection, provider);
        }

        public async Task SeedAsync()
        {
            await using var scope = services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<DemoUserSeeder>().SeedAsync();
        }

        public async Task<AuthenticatedUser> FindDemoUserAsync()
        {
            await using var scope = services.CreateAsyncScope();
            return (await scope.ServiceProvider.GetRequiredService<IdentityService>().FindUserByNameAsync(DemoModeOptions.DemoUserName))!;
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }

    // The production context maps domain tables with PostgreSQL-specific SQL; Identity alone is enough here.
    private sealed class IdentityOnlyContext(DbContextOptions<IdentityOnlyContext> options)
        : Microsoft.AspNetCore.Identity.EntityFrameworkCore.IdentityDbContext<AppUser, AppRole, Guid>(options);
}
