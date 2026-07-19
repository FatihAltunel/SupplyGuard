namespace SupplyGuard.Domain.Common;

public abstract class BaseEntity
{
    public Guid Id { get; private set; }

    protected BaseEntity()
    {
        // Required by EF Core.
    }

    protected BaseEntity(Guid id)
    {
        Id = id == Guid.Empty ? throw new ArgumentException("Entity ID cannot be empty.", nameof(id)) : id;
    }
}
