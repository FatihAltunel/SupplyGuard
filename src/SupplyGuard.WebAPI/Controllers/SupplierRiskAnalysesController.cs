using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SupplyGuard.WebAPI.RateLimiting;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Features.RiskManagement.GetRiskAnalysis;
using SupplyGuard.Application.Features.RiskManagement.RequestJitRiskAnalysis;
using SupplyGuard.Application.Security;

namespace SupplyGuard.WebAPI.Controllers;

[ApiController]
[Route("api/suppliers/{supplierId:guid}/risk-analyses")]
public sealed class SupplierRiskAnalysesController(
    ICommandHandler<RequestJitRiskAnalysisCommand, RequestJitRiskAnalysisResult> handler,
    IQueryHandler<GetRiskAnalysisQuery, RiskAnalysisStatusDto?> statusHandler) : ControllerBase
{
    /// <summary>Returns the current state of an analysis; poll until status is Completed or Failed.</summary>
    [HttpGet("{correlationId}")]
    [EnableRateLimiting(RateLimitPolicies.AnalysisStatus)]
    [Authorize(Policy = Permissions.RiskAssessmentsRead)]
    [ProducesResponseType<RiskAnalysisStatusDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskAnalysisStatusDto>> GetAnalysis(
        Guid supplierId, string correlationId, CancellationToken cancellationToken)
    {
        var status = await statusHandler.HandleAsync(new GetRiskAnalysisQuery(supplierId, correlationId), cancellationToken);
        return status is null ? NotFound() : Ok(status);
    }

    /// <summary>Records a pending analysis and its outbox message; inference runs asynchronously.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.AnalysisSubmit)]
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
            request.AverageDeliveryDelayDays, request.OnTimeDeliveryRate, request.QualityDefectRate,
            new JitFeatureOverrides(request.DeliveryDelayTrendPercentage, request.FinancialStabilityScore,
                request.ActiveRiskIndicatorsCount)), cancellationToken);
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

    /// <summary>Demo mode only; otherwise the request is rejected.</summary>
    [JsonPropertyName("deliveryDelayTrendPercentage")]
    public decimal? DeliveryDelayTrendPercentage { get; init; }

    /// <summary>Demo mode only; otherwise the request is rejected.</summary>
    [JsonPropertyName("financialStabilityScore")]
    public decimal? FinancialStabilityScore { get; init; }

    /// <summary>Demo mode only; otherwise the request is rejected.</summary>
    [JsonPropertyName("activeRiskIndicatorsCount")]
    public int? ActiveRiskIndicatorsCount { get; init; }
}
