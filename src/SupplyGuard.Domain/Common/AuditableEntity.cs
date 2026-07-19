namespace SupplyGuard.Domain.Common;

public abstract class AuditableEntity : BaseEntity
{
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? LastModifiedByUserId { get; private set; }
    public DateTimeOffset? LastModifiedAtUtc { get; private set; }

    protected AuditableEntity()
    {
        // Required by EF Core.
    }

    protected AuditableEntity(Guid? createdByUserId)
        : base(Guid.NewGuid())
    {
        CreatedByUserId = createdByUserId;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    protected void MarkAsModified(Guid? modifiedByUserId)
    {
        LastModifiedByUserId = modifiedByUserId;
        LastModifiedAtUtc = DateTimeOffset.UtcNow;
    }
}
