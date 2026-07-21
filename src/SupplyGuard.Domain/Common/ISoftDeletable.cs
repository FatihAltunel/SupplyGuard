namespace SupplyGuard.Domain.Common;

public interface ISoftDeletable
{
    bool IsDeleted { get; }
    DateTimeOffset? DeletedAtUtc { get; }

    void MarkAsDeleted(Guid? deletedByUserId = null);
}
