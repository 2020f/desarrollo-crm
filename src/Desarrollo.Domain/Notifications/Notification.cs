using Desarrollo.Domain.Common;

namespace Desarrollo.Domain.Notifications;

public class Notification : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public NotificationEvent Event { get; set; }
    public string Message { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? Route { get; set; }
    public bool IsRead { get; set; }
    public DateTime? ReadAt { get; set; }
    public DateTime? SnoozedUntil { get; set; }
    public string DedupKey { get; set; } = string.Empty;
}

public class NotificationPreference : Entity
{
    public Guid UserId { get; set; }
    public Guid? OrganizationId { get; set; }
    public NotificationEvent? Event { get; set; }
    public Guid? ProjectId { get; set; }
    public bool EmailEnabled { get; set; }
    public bool InAppEnabled { get; set; } = true;
    public bool DoNotDisturb { get; set; }
    public TimeSpan? QuietHoursStart { get; set; }
    public TimeSpan? QuietHoursEnd { get; set; }
}
