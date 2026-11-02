using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using SupplyGuard.Application;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Features.RiskManagement.RequestRiskAnalysis;
using SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;
using SupplyGuard.Application.Security;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;
using SupplyGuard.WebAPI.Controllers;
using Xunit;

namespace SupplyGuard.IntegrationTests;

public sealed class AnalyzeSupplierEndpointTests
{
    [Fact]
    public async Task Endpoint_ReturnsDurableAcceptedIdentifiers_AndMapsInput()
    {
        var supplierId = Guid.NewGuid();
        var expected = new RequestRiskAnalysisResult(supplierId, Guid.NewGuid(), Guid.NewGuid());
        var handler = new Mock<ICommandHandler<RequestRiskAnalysisCommand, RequestRiskAnalysisResult>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<RequestRiskAnalysisCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RequestRiskAnalysisResult>.Success(expected));
        var controller = new SuppliersController(null!, null!, null!, null!, null!, handler.Object);
        var input = RiskAnalysisFixtures.Requested().Input;
        var result = await controller.Analyze(supplierId, new(1, input.ObservedAtUtc, input.Features), CancellationToken.None);
        Assert.Equal(expected, Assert.IsType<AcceptedResult>(result.Result).Value);
        handler.Verify(value => value.HandleAsync(It.Is<RequestRiskAnalysisCommand>(command =>
            command.SupplierId == supplierId && command.Input.Features == input.Features && command.Input.FeatureSchemaVersion == 1),
            It.IsAny<CancellationToken>()), Times.Once);
        var method = typeof(SuppliersController).GetMethod(nameof(SuppliersController.Analyze))!;
        Assert.Equal("{id:guid}/analyze", Assert.Single(method.GetCustomAttributes(typeof(HttpPostAttribute), true).Cast<HttpPostAttribute>()).Template);
        Assert.Equal(Permissions.RiskAssessmentsCreate, Assert.Single(method.GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>()).Policy);
    }

    [Theory]
    [InlineData("Supplier.NotFound", typeof(NotFoundObjectResult))]
    [InlineData("RiskAnalysis.InvalidInput", typeof(BadRequestObjectResult))]
    public async Task Endpoint_MapsApplicationErrors(string code, Type expected)
    {
        var handler = new Mock<ICommandHandler<RequestRiskAnalysisCommand, RequestRiskAnalysisResult>>();
        handler.Setup(value => value.HandleAsync(It.IsAny<RequestRiskAnalysisCommand>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<RequestRiskAnalysisResult>.Failure(new Error(code, "Rejected")));
        var controller = new SuppliersController(null!, null!, null!, null!, null!, handler.Object);
        var input = RiskAnalysisFixtures.Requested().Input;
        var result = await controller.Analyze(Guid.NewGuid(), new(1, input.ObservedAtUtc, input.Features), CancellationToken.None);
        Assert.IsType(expected, result.Result);
    }

    [Fact]
    public async Task Infrastructure_ResolvesBothPureDiPipelines_WithoutConnectingToExternalServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:SupplyGuardDatabase"] = "Host=localhost;Port=1;Database=unused;Username=unused;Password=unused",
            ["RabbitMq:Password"] = "test-only"
        }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<ICurrentUserService>());
        services.AddSingleton(Mock.Of<Microsoft.Extensions.Caching.Distributed.IDistributedCache>());
        services.AddApplication();
        services.AddInfrastructure(configuration);
        services.AddRabbitMqMessaging(configuration);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICommandHandler<RequestRiskAnalysisCommand, RequestRiskAnalysisResult>>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<ICommandHandler<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>>());
        Assert.Single(provider.GetServices<Microsoft.Extensions.Hosting.IHostedService>().OfType<RiskAnalysisCompletedConsumer>());
        Assert.Same(provider.GetRequiredService<IMessagePublisher>(), scope.ServiceProvider.GetRequiredService<IMessagePublisher>());
    }
}
