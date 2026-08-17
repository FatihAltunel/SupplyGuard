namespace SupplyGuard.Application.Common.Interfaces;

public interface ICurrentUserService
{
    Guid? UserId { get; }
    bool IsAuthenticated { get; }
    string? UserName { get; }
    IReadOnlyCollection<string> Roles { get; }

    bool HasPermission(string permission);
}
