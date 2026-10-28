namespace Desarrollo.Application.Contracts;

public record ActivitySnapshotDto(DateTime ObservedAt, List<ChannelActivityDto> Channels,
    NotificationActivityDto Notifications);

public record ChannelActivityDto(Guid Id, Guid? LatestMessageId, Guid? LatestMessageAuthorId,
    DateTime? LatestMessageAt, DateTime? ChangedAt, int UnreadCount,
    Guid? LatestIncomingMessageId = null, DateTime? LatestIncomingMessageAt = null);

public record NotificationActivityDto(int UnreadCount, List<ActivityNotificationDto> Items);

public record ActivityNotificationDto(Guid Id, DateTime CreatedAt);
