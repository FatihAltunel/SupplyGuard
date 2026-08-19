namespace SupplyGuard.Domain.Common;

public interface IDomainEvent
{
    DateTimeOffset OccurredAtUtc { get; }
}
