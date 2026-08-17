namespace SupplyGuard.Application.Common.Models;

public sealed record IdentityOperationResult(bool Succeeded, IReadOnlyCollection<string> Errors)
{
    public static IdentityOperationResult Success() => new(true, Array.Empty<string>());

    public static IdentityOperationResult Failure(IEnumerable<string> errors) =>
        new(false, errors.ToArray());
}
