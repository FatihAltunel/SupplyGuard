using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;

namespace SupplyGuard.Application.Features.RiskManagement.RequestRiskAnalysis;

public sealed class CreateRequestRiskAnalysisCommandHandler(
    ISupplierRepository supplierRepository,
    IOutboxWriter outboxWriter,
    IOutboxDispatcher outboxDispatcher,
    IIntegrationMessageSerializer serializer,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider,
    ILogger<CreateRequestRiskAnalysisCommandHandler> logger)
    : ICommandHandler<RequestRiskAnalysisCommand, RequestRiskAnalysisResult>
{
    public async Task<Result<RequestRiskAnalysisResult>> HandleAsync(
        RequestRiskAnalysisCommand command,
        CancellationToken cancellationToken = default)
    {
        var supplier = await supplierRepository.GetByIdAsync(command.SupplierId, cancellationToken);
        if (supplier is null || supplier.IsDeleted)
        {
            return Result<RequestRiskAnalysisResult>.Failure(new Error("Supplier.NotFound", "The supplier was not found."));
        }

        // Freeze caller-owned feature data before staging the durable snapshot.
        var input = command.Input with
        {
            Features = new ReadOnlyDictionary<string, double?>(
                command.Input.Features.ToDictionary(feature => feature.Key, feature => feature.Value, StringComparer.Ordinal))
        };
        var requested = new RiskAnalysisRequestedEvent(
            Guid.NewGuid(), RiskAnalysisRequestedEvent.TypeName, Guid.NewGuid(),
            DateTimeOffset.FromUnixTimeMilliseconds(timeProvider.GetUtcNow().ToUnixTimeMilliseconds()),
            supplier.Id, input);
        if (serializer.Serialize(requested).Length > RiskAnalysisContractRules.MaximumPayloadLength)
        {
            return Result<RequestRiskAnalysisResult>.Failure(
                new Error("RiskAnalysis.PayloadTooLarge", "The request exceeds the audit payload limit."));
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            await outboxWriter.WriteAsync(requested, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }

        try
        {
            // Never hold the request transaction open while communicating with the broker.
            await outboxDispatcher.TryDispatchAsync(requested.MessageId, cancellationToken);
        }
        catch (Exception exception)
        {
            // Durable acceptance survives transport failure or cancellation after commit.
            // The background dispatcher must use the same stored message and identifiers.
            logger.LogWarning(
                "Immediate dispatch deferred for risk-analysis request {MessageId} after {FailureType}.",
                requested.MessageId, exception.GetType().Name);
        }

        return Result<RequestRiskAnalysisResult>.Success(
            new RequestRiskAnalysisResult(supplier.Id, requested.CorrelationId, requested.MessageId));
    }
}
