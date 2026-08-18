using SupplyGuard.Domain.Enums;

namespace SupplyGuard.Application.Features.Suppliers.GetSupplierById;

public sealed record SupplierDetailsDto(
    Guid Id,
    string Name,
    string TaxNumber,
    string CountryCode,
    string? RegistrationNumber,
    SupplierStatus Status,
    string? ContactName,
    string? ContactEmail,
    string? ContactPhone,
    string? WebsiteUrl,
    string? City,
    string? Address,
    string? Industry,
    string? SupplierCategory,
    bool IsCriticalSupplier,
    DateTimeOffset? OnboardingDateUtc,
    DateTimeOffset? LastRiskAssessmentAtUtc,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastModifiedAtUtc);
