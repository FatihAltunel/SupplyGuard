namespace SupplyGuard.Application.Common.CQRS;

public sealed record Error(string Code, string Description);

public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, IReadOnlyCollection<Error> errors)
    {
        IsSuccess = isSuccess;
        Value = value;
        Errors = errors;
    }

    public bool IsSuccess { get; }
    public T? Value { get; }
    public IReadOnlyCollection<Error> Errors { get; }

    public static Result<T> Success(T value) => new(true, value, []);

    public static Result<T> Failure(params Error[] errors)
    {
        ArgumentOutOfRangeException.ThrowIfZero(errors.Length);
        return new Result<T>(false, default, errors);
    }
}
