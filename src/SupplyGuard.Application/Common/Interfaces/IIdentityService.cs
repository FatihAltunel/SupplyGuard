using SupplyGuard.Application.Common.Models;

namespace SupplyGuard.Application.Common.Interfaces;

public interface IIdentityService
{
    Task<IdentityOperationResult> CreateUserAsync(
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedUser?> ValidateCredentialsAsync(
        string userNameOrEmail,
        string password,
        CancellationToken cancellationToken = default);

    Task<AuthenticatedUser?> FindUserByIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IdentityOperationResult> AssignRoleAsync(
        Guid userId,
        string role,
        CancellationToken cancellationToken = default);
}
