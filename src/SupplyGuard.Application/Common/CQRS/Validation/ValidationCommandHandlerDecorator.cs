using FluentValidation;

namespace SupplyGuard.Application.Common.CQRS.Validation;

public sealed class ValidationCommandHandlerDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> innerHandler,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
{
    public async Task<Result<TResult>> HandleAsync(TCommand command, CancellationToken cancellationToken = default)
    {
        var validationResults = await Task.WhenAll(
            validators.Select(validator => validator.ValidateAsync(command, cancellationToken)));

        var errors = validationResults
            .SelectMany(result => result.Errors)
            .Where(failure => failure is not null)
            .Select(failure => new Error(failure.ErrorCode, failure.ErrorMessage))
            .ToArray();

        return errors.Length > 0
            ? Result<TResult>.Failure(errors)
            : await innerHandler.HandleAsync(command, cancellationToken);
    }
}
