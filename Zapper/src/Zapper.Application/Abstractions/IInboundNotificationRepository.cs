using Zapper.Domain.Entities;
using Zapper.Domain.Enums;

namespace Zapper.Application.Abstractions;

public interface IInboundNotificationRepository
{
    Task AcquireDeduplicationLockAsync(NotificationSource source, string key, CancellationToken cancellationToken);

    Task<bool> ExistsAsync(NotificationSource source, string key, string? externalEventId, CancellationToken cancellationToken);

    Task AddAsync(InboundNotification notification, CancellationToken cancellationToken);
}