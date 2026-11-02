using Microsoft.EntityFrameworkCore;
using SupplyGuard.Application.Common.Interfaces;
using SupplyGuard.Application.Common.Messaging;

namespace SupplyGuard.Infrastructure.Persistence.Outbox;

public sealed class OutboxReader(SupplyGuardDbContext dbContext) : IOutboxReader
{
    public Task<OutboxMessage?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default) =>
        dbContext.OutboxMessages.AsNoTracking().SingleOrDefaultAsync(message => message.Id == messageId, cancellationToken);
}
