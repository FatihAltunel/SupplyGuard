namespace SupplyGuard.Application.Common.Models;

public sealed record AuthenticatedUser(
    Guid Id,
    string UserName,
    string? Email,
    IReadOnlyCollection<string> Roles,
    IReadOnlyCollection<string> Permissions);
