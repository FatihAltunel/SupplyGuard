using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.Suppliers.UpdateSupplier;

public sealed record UpdateSupplierCommand(
    Guid SupplierId,
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
    DateTimeOffset? OnboardingDateUtc) : ICommand<Guid>;
