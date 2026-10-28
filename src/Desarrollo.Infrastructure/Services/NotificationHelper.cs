using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Notifications;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public static class NotificationHelper
{
    public static async Task NotifyAsync(AppDbContext db, Guid orgId, Guid userId, NotificationEvent evt,
        string message, string entityType, Guid entityId, string? route, string dedupKey)
    {
        if (await db.Notifications.AnyAsync(n => n.UserId == userId && n.DedupKey == dedupKey)) return;
        db.Notifications.Add(new Notification
        {
            OrganizationId = orgId,
            UserId = userId,
            Event = evt,
            Message = message,
            EntityType = entityType,
            EntityId = entityId,
            Route = route,
            DedupKey = dedupKey,
        });
    }

    public static async Task NotifyMentionsAsync(AppDbContext db, Guid orgId, Guid authorId, string body,
        string entityType, Guid entityId, string? route)
    {
        var members = await db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == orgId && m.IsActive && m.UserId != authorId)
            .Select(m => new { m.UserId, m.Name }).ToListAsync();

        foreach (var m in members)
        {
            if (string.IsNullOrWhiteSpace(m.Name) || !ContainsMention(body, m.Name)) continue;
            var dedup = $"mention:{entityType}:{entityId}:{m.UserId}";
            if (await db.Notifications.AnyAsync(n => n.UserId == m.UserId && n.DedupKey == dedup)) continue;

            db.Notifications.Add(new Notification
            {
                OrganizationId = orgId,
                UserId = m.UserId,
                Event = NotificationEvent.Mencion,
                Message = "Te mencionaron en un comentario.",
                EntityType = entityType,
                EntityId = entityId,
                Route = route,
                DedupKey = dedup,
            });
            db.Mentions.Add(new Mention
            {
                OrganizationId = orgId,
                TargetUserId = m.UserId,
                AuthorId = authorId,
                EntityType = entityType,
                EntityId = entityId,
                Context = body,
            });
        }
    }

    private static bool ContainsMention(string body, string name)
    {
        if (body.Contains("@" + name, StringComparison.OrdinalIgnoreCase)) return true;
        var first = name.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return !string.IsNullOrEmpty(first) && first.Length >= 3 && body.Contains("@" + first, StringComparison.OrdinalIgnoreCase);
    }
}
