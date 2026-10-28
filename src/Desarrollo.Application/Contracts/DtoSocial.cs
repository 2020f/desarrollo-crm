namespace Desarrollo.Application.Contracts;

public record TicketDto(
    Guid Id, int Number, string Code, string Title,
    Guid? RequesterId, string? RequesterName, Desarrollo.Domain.TicketOrigin Origin,
    Desarrollo.Domain.TicketCategory Category, Desarrollo.Domain.Severity Severity, Desarrollo.Domain.Priority Priority,
    Guid? ProjectId, string? ProjectCode, Guid? AssigneeId,
    Desarrollo.Domain.TicketStatus Status, string? AffectedVersion, string? Environment,
    DateTime? ResolvedAt, DateTime CreatedAt,
    bool CanEdit = false, bool CanAssign = false, bool CanChangeStatus = false, bool CanComment = false,
    bool CanReadInternalNotes = false, bool CanWriteInternalNotes = false, bool CanLinkWorkItems = false);

public record TicketCommentDto(Guid Id, Guid AuthorId, string? AuthorName, string Body, bool IsInternal, DateTime CreatedAt);

public record CreateTicketRequest(
    string Title, Desarrollo.Domain.TicketCategory Category,
    string? Description, string? ReproSteps, Guid? ProductId, Guid? ProjectId,
    Desarrollo.Domain.Severity Severity, Desarrollo.Domain.Priority Priority,
    string? AffectedVersion, string? Environment, Guid? AssigneeId = null);

public record DocumentDto(Guid Id, string Title, Guid? ParentId, Guid AuthorId, List<string> Tags, DateTime UpdatedAt, string? Content = null,
    Guid ProjectId = default, Desarrollo.Domain.Docs.DocumentVisibility Visibility = Desarrollo.Domain.Docs.DocumentVisibility.Privada,
    IReadOnlyList<Guid>? ReaderIds = null, bool CanEdit = false, bool CanManageAccess = false,
    string ContentFormat = "plain", string RowVersion = "");

public record DocumentVersionDto(Guid Id, string Content, Guid AuthorId, DateTime CreatedAt, string? Note, string ContentFormat = "plain");

public record ChannelDto(Guid Id, string Name, string? Topic, Desarrollo.Domain.ChannelType Type, Guid? ProjectId, string? ProjectCode, bool IsMember, int? UnreadCount, Guid? DirectUserId = null);

public record MessageDto(Guid Id, Guid ChannelId, Guid AuthorId, string? AuthorName, Guid? ThreadRootId, string Body, bool IsEdited, DateTime CreatedAt);

public record NotificationDto(
    Guid Id, Desarrollo.Domain.NotificationEvent Event, string Message,
    string EntityType, Guid EntityId, string? Route, bool IsRead, DateTime CreatedAt, DateTime? SnoozedUntil);

public record CalendarEventDto(
    Guid Id, string Title, string? Description, Desarrollo.Domain.CalendarEventType Type,
    bool IsAllDay, DateTime Start, DateTime End, string? TimeZoneId,
    Desarrollo.Domain.RecurrenceFrequency? Recurrence, Guid? LinkedWorkItemId, Guid? LinkedMilestoneId, Guid? LinkedReleaseId,
    Guid? ProjectId, string? ProjectCode, Desarrollo.Domain.Priority? Priority, string? Color, Guid? AssigneeId, string? AssigneeName,
    string SourceType = "CalendarEvent", Guid? SourceId = null, string? DateKind = null, string? SourceRoute = null,
    bool IsDerived = false, bool CanEdit = true, bool CanDelete = true);

public record ReportSnapshotDto(
    int TotalTasks, int DoneTasks, int OverdueTasks, int BlockedTasks, int OpenTickets,
    double MedianCycleDays, int WipLimit, IReadOnlyList<ChartSlice> ByState);

public record ChartSlice(string Key, string Label, int Count);
