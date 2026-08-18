using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.Suppliers.GetSuppliers;

public sealed record SupplierListItemDto(
    Guid Id,
    string Name,
    string CountryCode,
    string TaxNumber,
    SupplierStatus Status,
    bool IsCriticalSupplier,
    string? City,
    string? Industry,
    DateTimeOffset? LastRiskAssessmentAtUtc);
