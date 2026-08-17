namespace SupplyGuard.Domain.Common;

public abstract class AuditableEntity : BaseEntity, ISoftDeletable
{
    public Guid? CreatedByUserId { get; private set; }
    public DateTimeOffset CreatedAtUtc { get; private set; }
    public Guid? LastModifiedByUserId { get; private set; }
    public DateTimeOffset? LastModifiedAtUtc { get; private set; }
    public bool IsDeleted { get; private set; }
    public DateTimeOffset? DeletedAtUtc { get; private set; }

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

    internal void UpdateAuditInfo(Guid? userId, DateTimeOffset utcNow, bool isNew)
    {
        var timestamp = utcNow.ToUniversalTime();

        if (isNew)
        {
            CreatedByUserId = userId;
            CreatedAtUtc = timestamp;
            return;
        }

        LastModifiedByUserId = userId;
        LastModifiedAtUtc = timestamp;
    }

    public void MarkAsDeleted(Guid? deletedByUserId = null)
    {
        if (IsDeleted)
        {
            return;
        }

        IsDeleted = true;
        DeletedAtUtc = DateTimeOffset.UtcNow;
        MarkAsModified(deletedByUserId);
    }
}
