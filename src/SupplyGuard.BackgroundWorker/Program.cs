using SupplyGuard.Application;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Infrastructure;
using SupplyGuard.Infrastructure.Messaging.RabbitMq;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddSingleton<ICurrentUserService, BackgroundCurrentUserService>();
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration, includeIdentityServices: false);
builder.Services.AddRabbitMqMessaging(builder.Configuration);
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration.GetConnectionString("Redis")
        ?? throw new InvalidOperationException("Connection string 'Redis' was not found.");
});
await builder.Build().RunAsync();

internal sealed class BackgroundCurrentUserService : ICurrentUserService
{
    public Guid? UserId => null;
    public bool IsAuthenticated => false;
    public string? UserName => "SupplyGuard.BackgroundWorker";
    public IReadOnlyCollection<string> Roles => Array.Empty<string>();
    public bool HasPermission(string permission) => false;
}
