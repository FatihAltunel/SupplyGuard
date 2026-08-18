using SupplyGuard.Application.Common.CQRS;

namespace SupplyGuard.Application.Features.Suppliers.CreateSupplier;

public sealed record CreateSupplierCommand(
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
    DateTimeOffset? OnboardingDateUtc) : ICommand<Guid>;
