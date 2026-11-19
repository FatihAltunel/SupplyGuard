using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;
using SupplyGuard.Application.Security;

namespace SupplyGuard.WebAPI.Controllers;

[ApiController]
[Route("api/suppliers/{supplierId:guid}/risk-analyses")]
public sealed class SupplierRiskAnalysesController(
    ICommandHandler<RequestJitRiskAnalysisCommand, RequestJitRiskAnalysisResult> handler) : ControllerBase
{
    /// <summary>Records a pending analysis and its outbox message; inference runs asynchronously.</summary>
    [HttpPost]
    [Authorize(Policy = Permissions.RiskAssessmentsCreate)]
    [RequestSizeLimit(16_384)]
    [ProducesResponseType<RequestJitRiskAnalysisResult>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<RequestJitRiskAnalysisResult>> RequestAnalysis(
        Guid supplierId, [FromBody] RequestSupplierRiskAnalysis request, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new(supplierId, request.CorrelationId,
            request.AverageDeliveryDelayDays, request.OnTimeDeliveryRate, request.QualityDefectRate), cancellationToken);
        if (result.IsSuccess) return Accepted(result.Value);
        if (HasError("Supplier.NotFound")) return NotFound(result.Errors);
        if (HasError("RiskAnalysis.CorrelationConflict")) return Conflict(result.Errors);
        if (HasError("RiskAnalysis.IntegrationsUnavailable")) return StatusCode(StatusCodes.Status503ServiceUnavailable, result.Errors);
        if (HasError("RiskAnalysis.TrendUnavailable") || HasError("RiskAnalysis.InvalidFeatures"))
            return UnprocessableEntity(result.Errors);
        return BadRequest(result.Errors);

        bool HasError(string code) => result.Errors.Any(error => error.Code == code);
    }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
[JsonNumberHandling(JsonNumberHandling.Strict)]
public sealed class RequestSupplierRiskAnalysis
{
    [Required, JsonRequired, JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = null!;

    [Required, JsonRequired, JsonPropertyName("averageDeliveryDelayDays")]
    public decimal AverageDeliveryDelayDays { get; init; }

    [Required, JsonRequired, JsonPropertyName("onTimeDeliveryRate")]
    public decimal OnTimeDeliveryRate { get; init; }

    [Required, JsonRequired, JsonPropertyName("qualityDefectRate")]
    public decimal QualityDefectRate { get; init; }
}
