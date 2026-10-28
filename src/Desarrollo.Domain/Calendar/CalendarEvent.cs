using Desarrollo.Domain.Common;
using Desarrollo.Domain.Projects;

namespace Desarrollo.Domain.Calendar;

public class CalendarEvent : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public CalendarEventType Type { get; set; } = CalendarEventType.Tarea;
    public bool IsAllDay { get; set; }
    public DateTime Start { get; set; }
    public DateTime End { get; set; }
    public string? TimeZoneId { get; set; }

    public Priority? Priority { get; set; }
    public string? Color { get; set; }
    public Guid? AssigneeId { get; set; }

    public RecurrenceFrequency? Recurrence { get; set; }
    public Guid? SeriesId { get; set; }
    public DateTime? SeriesStart { get; set; }
    public bool IsCancelled { get; set; }
    public string? CancellationNote { get; set; }

    public Guid? LinkedWorkItemId { get; set; }
    public Guid? LinkedMilestoneId { get; set; }
    public Guid? LinkedReleaseId { get; set; }
}
