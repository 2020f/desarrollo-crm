using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

/// <summary>Small, read-only activity snapshot; the ordinary services remain the authorization boundary.</summary>
public sealed class LiveActivityService(AppDbContext db, ICurrentUser user, IChannelService channels,
    INotificationService notifications) : ILiveActivityService
{
    public async Task<ActivitySnapshotDto> GetAsync()
    {
        // Capture before any query. A message committed during this request must not be
        // mistaken for historical activity by the next client observation.
        var observedAt = DateTime.UtcNow;
        var userId = user.UserId ?? throw new ForbiddenException();
        var organizationId = user.OrganizationId ?? throw new ForbiddenException();
        if (!user.IsAuthenticated
            || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive)
            || !await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == organizationId
                && m.UserId == userId && m.IsActive))
            throw new ForbiddenException("La cuenta o membresía de la organización ya no está activa.");

        var readableChannels = await channels.ListAsync(null);
        var channelIds = readableChannels.Select(c => c.Id).ToList();
        var activity = new List<ChannelActivityDto>();
        if (channelIds.Count > 0)
        {
            // Preserve the same valid-root rule as GetMessagesAsync. Neither deleted
            // messages nor orphaned/deleted thread replies contribute to any metadata.
            var messages = db.Messages.AsNoTracking().Where(m => channelIds.Contains(m.ChannelId) && !m.IsDeleted
                && (!m.ThreadRootId.HasValue || db.Messages.Any(root => root.Id == m.ThreadRootId
                    && root.ChannelId == m.ChannelId && !root.IsDeleted && !root.ThreadRootId.HasValue)));
            var latest = await messages.GroupBy(m => m.ChannelId)
                .Select(group => group.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                    .Select(m => new { m.ChannelId, m.Id, m.AuthorId, m.CreatedAt }).First()).ToListAsync();
            // Keep incoming activity separately: an outgoing reply during the same
            // polling interval must not hide a new message from somebody else.
            var incoming = await messages.Where(m => m.AuthorId != userId).GroupBy(m => m.ChannelId)
                .Select(group => group.OrderByDescending(m => m.CreatedAt).ThenByDescending(m => m.Id)
                    .Select(m => new { m.ChannelId, m.Id, m.CreatedAt }).First()).ToListAsync();
            var changes = await messages.GroupBy(m => m.ChannelId)
                .Select(group => new { ChannelId = group.Key,
                    ChangedAt = group.Max(m => m.UpdatedAt > m.CreatedAt ? m.UpdatedAt : m.CreatedAt) }).ToListAsync();
            var unreadMessages = from message in messages
                join member in db.ChannelMembers.AsNoTracking().Where(m => m.UserId == userId)
                    on message.ChannelId equals member.ChannelId into memberships
                from member in memberships.DefaultIfEmpty()
                where message.AuthorId != userId
                    && (member == null || member.LastReadAt == null || message.CreatedAt > member.LastReadAt)
                select message;
            var unread = await unreadMessages.GroupBy(m => m.ChannelId)
                .Select(group => new { ChannelId = group.Key, Count = group.Count() }).ToListAsync();
            var latestByChannel = latest.ToDictionary(m => m.ChannelId);
            var incomingByChannel = incoming.ToDictionary(m => m.ChannelId);
            var changedByChannel = changes.ToDictionary(c => c.ChannelId, c => c.ChangedAt);
            var unreadByChannel = unread.ToDictionary(c => c.ChannelId, c => c.Count);
            foreach (var channel in readableChannels)
            {
                latestByChannel.TryGetValue(channel.Id, out var message);
                incomingByChannel.TryGetValue(channel.Id, out var received);
                activity.Add(new(channel.Id, message?.Id, message?.AuthorId,
                    message is null ? null : Utc(message.CreatedAt),
                    changedByChannel.TryGetValue(channel.Id, out var changedAt) ? Utc(changedAt) : null,
                    unreadByChannel.GetValueOrDefault(channel.Id), received?.Id,
                    received is null ? null : Utc(received.CreatedAt)));
            }
        }

        // This service rechecks resource access, including private documents/chats,
        // rather than exposing raw notification rows or an unfiltered count.
        var unreadNotifications = await notifications.ListAsync(true, 1, 100);
        return new(observedAt, activity, new(unreadNotifications.Total,
            unreadNotifications.Items.Select(n => new ActivityNotificationDto(n.Id, Utc(n.CreatedAt))).ToList()));
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
}
