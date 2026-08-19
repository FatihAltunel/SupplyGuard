using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using SupplyGuard.Application.Common.CQRS;
using SupplyGuard.Application.Common.CQRS.Validation;
using SupplyGuard.Application.Common.Events;
using SupplyGuard.Application.Features.RiskManagement.AcknowledgeEarlyWarning;
using SupplyGuard.Application.Features.RiskManagement.EvaluateSupplierRisk;
using SupplyGuard.Application.Features.RiskManagement.Events;
using SupplyGuard.Application.Features.RiskManagement.GetActiveEarlyWarnings;
using SupplyGuard.Application.Features.RiskManagement.GetSupplierCurrentRisk;
using SupplyGuard.Application.Features.Suppliers.ChangeSupplierStatus;
using SupplyGuard.Application.Features.Suppliers.CreateSupplier;
using SupplyGuard.Application.Features.Suppliers.GetSupplierById;
using SupplyGuard.Application.Features.Suppliers.GetSuppliers;
using SupplyGuard.Application.Features.Suppliers.UpdateSupplier;
using SupplyGuard.Domain.Events;
using SupplyGuard.Domain.Services;

namespace SupplyGuard.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IRiskAssessmentService, RiskAssessmentService>();

        services.AddScoped<IValidator<CreateSupplierCommand>, CreateSupplierCommandValidator>();
        services.AddValidatedCommandHandler<CreateSupplierCommand, Guid, CreateSupplierCommandHandler>();

        services.AddScoped<IValidator<UpdateSupplierCommand>, UpdateSupplierCommandValidator>();
        services.AddValidatedCommandHandler<UpdateSupplierCommand, Guid, UpdateSupplierCommandHandler>();

        services.AddScoped<IValidator<ChangeSupplierStatusCommand>, ChangeSupplierStatusCommandValidator>();
        services.AddValidatedCommandHandler<ChangeSupplierStatusCommand, Guid, ChangeSupplierStatusCommandHandler>();

        services.AddScoped<IQueryHandler<GetSupplierByIdQuery, SupplierDetailsDto?>, GetSupplierByIdQueryHandler>();
        services.AddScoped<IQueryHandler<GetSuppliersQuery, PagedResult<SupplierListItemDto>>, GetSuppliersQueryHandler>();

        services.AddScoped<IValidator<EvaluateSupplierRiskCommand>, EvaluateSupplierRiskCommandValidator>();
        services.AddValidatedCommandHandler<EvaluateSupplierRiskCommand, EvaluateSupplierRiskResult, EvaluateSupplierRiskCommandHandler>();

        services.AddScoped<IValidator<AcknowledgeEarlyWarningCommand>, AcknowledgeEarlyWarningCommandValidator>();
        services.AddValidatedCommandHandler<AcknowledgeEarlyWarningCommand, Guid, AcknowledgeEarlyWarningCommandHandler>();

        services.AddScoped<IQueryHandler<GetSupplierCurrentRiskQuery, SupplierCurrentRiskDto?>, GetSupplierCurrentRiskQueryHandler>();
        services.AddScoped<IQueryHandler<GetActiveEarlyWarningsQuery, IReadOnlyList<ActiveEarlyWarningDto>>, GetActiveEarlyWarningsQueryHandler>();

        services.AddScoped<IDomainEventHandler<SupplierRiskAssessedEvent>, SupplierRiskAssessedEventHandler>();

        return services;
    }

    private static IServiceCollection AddValidatedCommandHandler<TCommand, TResult, THandler>(this IServiceCollection services)
        where TCommand : class, ICommand<TResult>
        where THandler : class, ICommandHandler<TCommand, TResult>
    {
        services.AddScoped<THandler>();
        services.AddScoped<ICommandHandler<TCommand, TResult>>(serviceProvider =>
            new ValidationCommandHandlerDecorator<TCommand, TResult>(
                serviceProvider.GetRequiredService<THandler>(),
                serviceProvider.GetServices<IValidator<TCommand>>()));

        return services;
    }
}
