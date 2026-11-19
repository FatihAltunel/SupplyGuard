using System.Text.Json;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.RiskManagement.ProcessRiskAnalysisResponse;

public sealed class ProcessRiskAnalysisResponseCommandHandler(
    IXAIAuditLogRepository repository,
    IRiskAnalysisResponseSerializer serializer,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider)
    : ICommandHandler<ProcessRiskAnalysisResponseCommand, ProcessRiskAnalysisResponseResult>
{
    public async Task<Result<ProcessRiskAnalysisResponseResult>> HandleAsync(
        ProcessRiskAnalysisResponseCommand command, CancellationToken cancellationToken = default)
    {
        string payload;
        try { payload = serializer.Serialize(command.Response); }
        catch (JsonException) { return Failure("RiskAnalysis.InvalidResponse"); }

        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var audit = await repository.GetByCorrelationIdForUpdateAsync(command.Response.CorrelationId, cancellationToken);
            if (audit is null || audit.SupplierId != command.Response.SupplierId || audit.RequestMessageId is null)
            {
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return Failure("RiskAnalysis.RequestMismatch");
            }
            if (audit.ExplanationStatus != ExplanationStatus.Pending)
            {
                // Compare full-precision semantic values retained in the response snapshot, not the rounded DB score.
                var identical = audit.ResponsePayload is not null && serializer.Deserialize(audit.ResponsePayload) == command.Response;
                await unitOfWork.RollbackAsync(CancellationToken.None);
                return identical ? Success(RiskAnalysisResponseDisposition.Duplicate) : Failure("RiskAnalysis.ConflictingResponse");
            }

            var now = timeProvider.GetUtcNow();
            if (command.Response.IsSuccessful)
                audit.Complete(command.Response.ConfidenceScore, command.Response.ExplanationText, payload, now);
            else
                audit.Fail(command.Response.ErrorMessage!, payload, now);
            await unitOfWork.CommitAsync(cancellationToken);
            return Success(RiskAnalysisResponseDisposition.Processed);
        }
        catch
        {
            await unitOfWork.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static Result<ProcessRiskAnalysisResponseResult> Success(RiskAnalysisResponseDisposition disposition) =>
        Result<ProcessRiskAnalysisResponseResult>.Success(new(disposition));
    private static Result<ProcessRiskAnalysisResponseResult> Failure(string code) =>
        Result<ProcessRiskAnalysisResponseResult>.Failure(new Error(code, "The response does not match a valid pending analysis or its retained result."));
}
