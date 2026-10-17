using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SupplyGuard.Infrastructure.Persistence.Outbox;

public sealed class OutboxProcessor(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxProcessorOptions> options,
    TimeProvider timeProvider,
    ILogger<OutboxProcessor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // In .NET 8, yield so the first polling iteration cannot delay host startup.
        await Task.Yield();

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Hosted services are singletons; never retain the scoped dispatcher or DbContext.
                await using var scope = scopeFactory.CreateAsyncScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<OutboxDispatcher>();
                await dispatcher.DispatchBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                // Discard this scope after a database/save failure; do not reuse its tracked state.
                logger.LogError(
                    "Outbox polling iteration failed with {FailureType}; retrying after the polling interval",
                    exception.GetType().Name);
            }

            try
            {
                // The scope has been disposed before waiting. Iterations never overlap.
                await Task.Delay(options.Value.PollInterval, timeProvider, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
