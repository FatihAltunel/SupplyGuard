using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Features.Suppliers.ChangeSupplierStatus;
using SupplyGuard.Application.Features.Suppliers.CreateSupplier;
using SupplyGuard.Application.Features.Suppliers.GetSupplierById;
using SupplyGuard.Application.Features.Suppliers.GetSuppliers;
using SupplyGuard.Application.Features.Suppliers.UpdateSupplier;
using SupplyGuard.Application.Security;
using SupplyGuard.Domain.Enums;

namespace SupplyGuard.WebAPI.Controllers;

[ApiController]
[Route("api/suppliers")]
public sealed class SuppliersController(
    ICommandHandler<CreateSupplierCommand, Guid> createSupplierHandler,
    ICommandHandler<UpdateSupplierCommand, Guid> updateSupplierHandler,
    ICommandHandler<ChangeSupplierStatusCommand, Guid> changeSupplierStatusHandler,
    IQueryHandler<GetSupplierByIdQuery, SupplierDetailsDto?> getSupplierByIdHandler,
    IQueryHandler<GetSuppliersQuery, PagedResult<SupplierListItemDto>> getSuppliersHandler) : ControllerBase
{
    [HttpPost]
    [Authorize(Policy = Permissions.SuppliersCreate)]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<Guid>> Create(
        [FromBody] CreateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await createSupplierHandler.HandleAsync(
            new CreateSupplierCommand(
                request.Name,
                request.TaxNumber,
                request.CountryCode,
                request.RegistrationNumber,
                request.ContactName,
                request.ContactEmail,
                request.ContactPhone,
                request.WebsiteUrl,
                request.City,
                request.Address,
                request.Industry,
                request.SupplierCategory,
                request.IsCriticalSupplier,
                request.OnboardingDateUtc),
            cancellationToken);

        if (!result.IsSuccess)
        {
            return HasError(result, "Supplier.BusinessKeyAlreadyExists")
                ? Conflict(result.Errors)
                : BadRequest(result.Errors);
        }

        return CreatedAtAction(nameof(GetById), new { id = result.Value }, result.Value);
    }

    [HttpPut("{id:guid}")]
    [Authorize(Policy = Permissions.SuppliersUpdate)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Guid>> Update(
        Guid id,
        [FromBody] UpdateSupplierRequest request,
        CancellationToken cancellationToken)
    {
        var result = await updateSupplierHandler.HandleAsync(
            new UpdateSupplierCommand(
                id,
                request.RegistrationNumber,
                request.ContactName,
                request.ContactEmail,
                request.ContactPhone,
                request.WebsiteUrl,
                request.City,
                request.Address,
                request.Industry,
                request.SupplierCategory,
                request.IsCriticalSupplier,
                request.OnboardingDateUtc),
            cancellationToken);

        return ToCommandResponse(result);
    }

    [HttpPatch("{id:guid}/status")]
    [Authorize(Policy = Permissions.SuppliersChangeStatus)]
    [ProducesResponseType<Guid>(StatusCodes.Status200OK)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<IReadOnlyCollection<Error>>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<Guid>> ChangeStatus(
        Guid id,
        [FromBody] ChangeSupplierStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await changeSupplierStatusHandler.HandleAsync(
            new ChangeSupplierStatusCommand(id, request.Status),
            cancellationToken);

        return ToCommandResponse(result);
    }

    [HttpGet("{id:guid}")]
    [Authorize(Policy = Permissions.SuppliersRead)]
    [ProducesResponseType<SupplierDetailsDto>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SupplierDetailsDto>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var supplier = await getSupplierByIdHandler.HandleAsync(
            new GetSupplierByIdQuery(id),
            cancellationToken);

        return supplier is null ? NotFound() : Ok(supplier);
    }

    [HttpGet]
    [Authorize(Policy = Permissions.SuppliersRead)]
    [ProducesResponseType<PagedResult<SupplierListItemDto>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<SupplierListItemDto>>> GetAll(
        [FromQuery] int pageNumber = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var suppliers = await getSuppliersHandler.HandleAsync(
            new GetSuppliersQuery(pageNumber, pageSize),
            cancellationToken);

        return Ok(suppliers);
    }

    private ActionResult<Guid> ToCommandResponse(Result<Guid> result)
    {
        if (result.IsSuccess)
        {
            return Ok(result.Value);
        }

        return HasError(result, "Supplier.NotFound")
            ? NotFound(result.Errors)
            : BadRequest(result.Errors);
    }

    private static bool HasError<T>(Result<T> result, string errorCode) =>
        result.Errors.Any(error => error.Code == errorCode);

    public sealed record CreateSupplierRequest(
        string Name,
        string TaxNumber,
        string CountryCode,
        string? RegistrationNumber,
        string? ContactName,
        string? ContactEmail,
        string? ContactPhone,
        string? WebsiteUrl,
        string? City,
        string? Address,
        string? Industry,
        string? SupplierCategory,
        bool IsCriticalSupplier,
        DateTimeOffset? OnboardingDateUtc);

    public sealed record UpdateSupplierRequest(
        string? RegistrationNumber,
        string? ContactName,
        string? ContactEmail,
        string? ContactPhone,
        string? WebsiteUrl,
        string? City,
        string? Address,
        string? Industry,
        string? SupplierCategory,
        bool IsCriticalSupplier,
        DateTimeOffset? OnboardingDateUtc);

    public sealed record ChangeSupplierStatusRequest(SupplierStatus Status);
}
