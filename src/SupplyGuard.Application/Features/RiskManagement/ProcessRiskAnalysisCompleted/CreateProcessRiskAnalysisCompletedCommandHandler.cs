using System.Text.Json;
using FluentValidation;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Exceptions;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging.RiskAnalysis.V1;
using SupplyGuard.Domain.Entities;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisCompleted;

public sealed class CreateProcessRiskAnalysisCompletedCommandHandler(
    IRiskAnalysisRepository repository,
    IOutboxReader outboxReader,
    IIntegrationMessageSerializer serializer,
    IValidator<RiskAnalysisRequestedEvent> requestValidator,
    IUnitOfWork unitOfWork)
    : ICommandHandler<ProcessRiskAnalysisCompletedCommand, ProcessRiskAnalysisCompletedResult>
{
    public async Task<Result<ProcessRiskAnalysisCompletedResult>> HandleAsync(
        ProcessRiskAnalysisCompletedCommand command,
        CancellationToken cancellationToken = default)
    {
        var completion = command.Completion;
        var correlationId = completion.CorrelationId.ToString("D");
        if (await repository.AuditExistsByCorrelationIdAsync(correlationId, cancellationToken))
        {
            return Duplicate(completion.SupplierId);
        }

        var snapshot = await outboxReader.GetByMessageIdAsync(completion.RequestMessageId, cancellationToken);
        if (snapshot is null)
        {
            return Failure("RiskAnalysis.RequestNotFound", "No retained request matches this completion.");
        }

        RiskAnalysisRequestedEvent requested;
        try
        {
            requested = serializer.Deserialize<RiskAnalysisRequestedEvent>(snapshot.Payload);
        }
        catch (JsonException)
        {
            return Failure("RiskAnalysis.InvalidRequestSnapshot", "The retained request is not a valid v1 message.");
        }

        var validation = await requestValidator.ValidateAsync(requested, cancellationToken);
        if (!validation.IsValid ||
            snapshot.MessageType != RiskAnalysisRequestedEvent.TypeName ||
            requested.MessageId != snapshot.Id ||
            requested.MessageId != completion.RequestMessageId ||
            snapshot.CorrelationId != correlationId ||
            requested.CorrelationId != completion.CorrelationId ||
            requested.OccurredAtUtc != snapshot.OccurredAtUtc ||
            requested.SupplierId != completion.SupplierId)
        {
            return Failure("RiskAnalysis.RequestMismatch", "Completion metadata does not match the retained request.");
        }

        var responsePayload = serializer.Serialize(completion);
        if (snapshot.Payload.Length > RiskAnalysisContractRules.MaximumPayloadLength ||
            responsePayload.Length > RiskAnalysisContractRules.MaximumPayloadLength)
        {
            return Failure("RiskAnalysis.PayloadTooLarge", "The message exceeds the audit payload limit.");
        }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            // Serialize concurrent results for this supplier. Recheck after taking the lock.
            var supplier = await repository.GetSupplierForAnalysisAsync(completion.SupplierId, cancellationToken);
            if (supplier is null || supplier.IsDeleted)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return Failure("Supplier.NotFound", "The supplier was not found.");
            }

            if (await repository.AuditExistsByCorrelationIdAsync(correlationId, cancellationToken))
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return Duplicate(completion.SupplierId);
            }

            var succeeded = completion.Status == RiskAnalysisContractRules.Succeeded;
            var assessment = succeeded
                ? RiskAssessment.FromModelScore(
                    supplier.Id, completion.Result!.RiskScore, completion.Execution.CompletedAtUtc,
                    completion.Result.Explanation.Reason,
                    outcome: completion.Model.IsMock ? "Mock model result" : "Model result")
                : null;
            var audit = new XAIAuditLog(
                supplier.Id,
                completion.Model.Name,
                completion.Model.Version,
                correlationId,
                snapshot.Payload,
                responsePayload,
                succeeded ? completion.Result!.ConfidenceScore : 0m,
                completion.Execution.LatencyMs,
                succeeded,
                completion.Execution.CompletedAtUtc,
                riskAssessmentId: assessment?.Id,
                failureCode: completion.Error?.Code,
                failureMessage: completion.Error?.Message,
                explanationStatus: succeeded ? ExplanationStatus.Completed : ExplanationStatus.Failed);

            if (assessment is not null)
            {
                supplier.AddRiskAssessment(assessment);
                supplier.AddXAIAuditLog(audit);
            }

            // A terminal failure inserts only its audit; the supplier is left unchanged.
            await repository.AddXAIAuditLogAsync(audit, cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return Result<ProcessRiskAnalysisCompletedResult>.Success(new(
                supplier.Id, assessment?.Id, RiskAnalysisCompletionDisposition.Processed));
        }
        catch (DuplicateCorrelationException exception) when (exception.CorrelationId == completion.CorrelationId)
        {
            // The unique database constraint is the final guard for cross-process races.
            await unitOfWork.RollbackAsync(CancellationToken.None);
            return Duplicate(completion.SupplierId);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static Result<ProcessRiskAnalysisCompletedResult> Duplicate(Guid supplierId) =>
        Result<ProcessRiskAnalysisCompletedResult>.Success(new(supplierId, null, RiskAnalysisCompletionDisposition.Duplicate));

    private static Result<ProcessRiskAnalysisCompletedResult> Failure(string code, string description) =>
        Result<ProcessRiskAnalysisCompletedResult>.Failure(new Error(code, description));
}
