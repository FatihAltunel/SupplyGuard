using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Moq;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using SupplyGuard.Infrastructure.Persistence.Outbox;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class BackgroundWorkerRegistrationTests
{
    [Fact]
    public async Task BackgroundHost_ValidatesScopesAndAllServices_WithoutIdentityOrBrokerConnection()
    {
        var configuration = Configuration();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new Mock<ICurrentUserService>().Object);
        services.AddDistributedMemoryCache();
        services.AddApplication();
        services.AddInfrastructure(configuration, includeIdentityServices: false);
        services.AddRabbitMqMessaging(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        var hosted = provider.GetServices<IHostedService>().ToArray();
        Assert.Single(hosted.OfType<RiskAnalysisResponseConsumer>());
        Assert.Single(hosted.OfType<OutboxProcessor>());
    }

    [Fact]
    public async Task ApiHost_CanRegisterMessagingWithoutRunningAnyBackgroundConsumers()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddRabbitMqMessaging(Configuration(), runBackgroundServices: false);
        await using var provider = services.BuildServiceProvider();
        Assert.Empty(provider.GetServices<IHostedService>());
        Assert.NotNull(provider.GetRequiredService<IMessagePublisher>());
    }

    private static IConfiguration Configuration() => new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["ConnectionStrings:SupplyGuardDatabase"] = "Host=localhost;Database=unused;Username=unused;Password=unused",
        ["RabbitMq:Password"] = "test-only"
    }).Build();
}
