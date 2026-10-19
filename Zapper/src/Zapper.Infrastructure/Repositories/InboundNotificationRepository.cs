using Microsoft.EntityFrameworkCore;
using Zapper.Application.Abstractions;
using Zapper.Domain.Entities;
using Zapper.Domain.Enums;
using Zapper.Infrastructure.Persistence;

namespace Zapper.Infrastructure.Repositories;

public sealed class InboundNotificationRepository(ZapperDbContext dbContext) : IInboundNotificationRepository
{
    public async Task AcquireDeduplicationLockAsync(NotificationSource source, string key, CancellationToken cancellationToken) =>
        _ = await dbContext.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(hashtext({source.ToString()}), hashtext({key}))", cancellationToken);

    public Task<bool> ExistsAsync(NotificationSource source, string key, string? externalEventId, CancellationToken cancellationToken) =>
        dbContext.InboundNotifications.AnyAsync(x => x.Source == source &&
            (x.DeduplicationKey == key || externalEventId != null && x.ExternalEventId == externalEventId), cancellationToken);

    public Task AddAsync(InboundNotification notification, CancellationToken cancellationToken) =>
        dbContext.InboundNotifications.AddAsync(notification, cancellationToken).AsTask();
}