using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Features.RiskManagement.AcknowledgeEarlyWarning;
using SupplyGuard.Application.Features.RiskManagement.EvaluateSupplierRisk;
using SupplyGuard.Application.Features.RiskManagement.GetActiveEarlyWarnings;
using SupplyGuard.Application.Features.RiskManagement.GetSupplierCurrentRisk;
using SupplyGuard.Application.Security;

namespace SupplyGuard.WebAPI.Controllers;

[ApiController]
[Route("api/risk-management")]
public sealed class RiskManagementController(
    ICommandHandler<EvaluateSupplierRiskCommand, EvaluateSupplierRiskResult> evaluateSupplierRiskHandler,
    ICommandHandler<AcknowledgeEarlyWarningCommand, Guid> acknowledgeEarlyWarningHandler,
    IQueryHandler<GetSupplierCurrentRiskQuery, SupplierCurrentRiskDto?> getSupplierCurrentRiskHandler,
    IQueryHandler<GetActiveEarlyWarningsQuery, IReadOnlyList<ActiveEarlyWarningDto>> getActiveEarlyWarningsHandler)
    : ControllerBase
{
    [HttpPost("suppliers/{supplierId:guid}/evaluations")]
    [Authorize(Policy = Permissions.RiskAssessmentsCreate)]
    [ProducesResponseType<EvaluateSupplierRiskResult>(StatusCodes.Status201Created)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<EvaluateSupplierRiskResult>> EvaluateSupplierRisk(
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        var result = await evaluateSupplierRiskHandler.HandleAsync(
            new EvaluateSupplierRiskCommand(supplierId, HttpContext.TraceIdentifier),
            cancellationToken);

        if (result.IsSuccess)
        {
            return CreatedAtAction(
                nameof(GetSupplierCurrentRisk),
                new { supplierId },
                result.Value);
        }

        if (HasError(result, "Supplier.NotFound"))
        {
            return NotFound(result.Errors);
        }

        return HasError(result, "RiskAssessment.NotEvaluable")
            ? UnprocessableEntity(result.Errors)
            : BadRequest(result.Errors);
    }

    [HttpPatch("suppliers/{supplierId:guid}/warnings/{earlyWarningId:guid}/acknowledgement")]
    [Authorize(Policy = Permissions.EarlyWarningsManage)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Guid>> AcknowledgeEarlyWarning(
        Guid supplierId,
        Guid earlyWarningId,
        CancellationToken cancellationToken)
    {
        var result = await acknowledgeEarlyWarningHandler.HandleAsync(
            new AcknowledgeEarlyWarningCommand(supplierId, earlyWarningId),
            cancellationToken);

        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        if (HasError(result, "Supplier.NotFound") || HasError(result, "EarlyWarning.NotFound"))
        {
            return NotFound(result.Errors);
        }

        if (HasError(result, "User.NotAuthenticated"))
        {
            return Unauthorized(result.Errors);
        }

        return HasError(result, "EarlyWarning.InvalidTransition")
            ? Conflict(result.Errors)
            : BadRequest(result.Errors);
    }

    [HttpGet("suppliers/{supplierId:guid}/current")]
    [Authorize(Policy = Permissions.RiskAssessmentsRead)]
    [ProducesResponseType<SupplierCurrentRiskDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierCurrentRiskDto>> GetSupplierCurrentRisk(
        Guid supplierId,
        CancellationToken cancellationToken)
    {
        var risk = await getSupplierCurrentRiskHandler.HandleAsync(
            new GetSupplierCurrentRiskQuery(supplierId),
            cancellationToken);

        return risk is null ? NotFound() : Ok(risk);
    }

    [HttpGet("warnings/active")]
    [Authorize(Policy = Permissions.RiskAssessmentsRead)]
    [ProducesResponseType<IReadOnlyList<ActiveEarlyWarningDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ActiveEarlyWarningDto>>> GetActiveEarlyWarnings(
        [FromQuery] Guid? supplierId = null,
        CancellationToken cancellationToken = default)
    {
        var warnings = await getActiveEarlyWarningsHandler.HandleAsync(
            new GetActiveEarlyWarningsQuery(supplierId),
            cancellationToken);

        return Ok(warnings);
    }

    private static bool HasError<T>(Result<T> result, string errorCode) =>
        result.Errors.Any(error => error.Code == errorCode);
}
