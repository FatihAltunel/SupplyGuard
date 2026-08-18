using FluentValidation;

namespace SupplyGuard.Application.Features.Suppliers.CreateSupplier;

public sealed class CreateSupplierCommandValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierCommandValidator()
    {
        RuleFor(command => command.Name).NotEmpty().MaximumLength(200);
        RuleFor(command => command.TaxNumber).NotEmpty().MaximumLength(64);
        RuleFor(command => command.CountryCode).Matches("^[A-Za-z]{2}$");
        RuleFor(command => command.RegistrationNumber).MaximumLength(64);
        RuleFor(command => command.ContactName).MaximumLength(150);
        RuleFor(command => command.ContactEmail).EmailAddress().MaximumLength(320).When(command => !string.IsNullOrWhiteSpace(command.ContactEmail));
        RuleFor(command => command.ContactPhone).MaximumLength(32);
        RuleFor(command => command.WebsiteUrl).MaximumLength(2048).Must(BeAnAbsoluteUri).When(command => !string.IsNullOrWhiteSpace(command.WebsiteUrl));
        RuleFor(command => command.City).MaximumLength(100);
        RuleFor(command => command.Address).MaximumLength(500);
        RuleFor(command => command.Industry).MaximumLength(100);
        RuleFor(command => command.SupplierCategory).MaximumLength(100);
    }

    private static bool BeAnAbsoluteUri(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
