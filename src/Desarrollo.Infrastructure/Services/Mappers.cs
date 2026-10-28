using Desarrollo.Application.Contracts;
using Desarrollo.Domain.Calendar;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Docs;
using Desarrollo.Domain.Notifications;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Tickets;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public static class UserNames
{
    public static async Task<Dictionary<Guid, string>> ResolveAsync(AppDbContext db, IEnumerable<Guid?> ids)
    {
        var set = new HashSet<Guid>();
        foreach (var id in ids) if (id.HasValue) set.Add(id.Value);
        if (set.Count == 0) return new();
        return await db.Users.Where(u => set.Contains(u.Id))
            .Select(u => new { u.Id, u.FullName })
            .ToDictionaryAsync(u => u.Id, u => string.IsNullOrWhiteSpace(u.FullName) ? u.Id.ToString("N")[..8] : u.FullName);
    }
}

public static class Mappers
{
    public static OrganizationDto ToDto(this Organization o) =>
        new(o.Id, o.Name, o.Slug, o.TimeZoneId);

    public static MemberDto ToDto(this OrganizationMember m, string? roleName) =>
        new(m.Id, m.UserId, m.Name, m.IsActive, roleName);

    public static ProductDto ToDto(this Product p) =>
        new(p.Id, p.Name, p.Description, p.IsActive);

    public static ProjectDto ToDto(this Project p) =>
        new(p.Id, p.Code, p.Name, p.Objective, p.Status, p.Priority, p.LeaderId, p.DueDate, null, p.IsPrivate);

    public static WorkflowStateDto ToDto(this WorkflowState s) =>
        new(s.Id, s.Name, s.Category, s.Order, s.Color, s.IsDefault);

    public static EpicDto ToDto(this Epic e) =>
        new(e.Id, e.Title, e.Goal, e.OwnerId, e.Priority, e.DueDate);

    public static CycleDto ToDto(this Cycle c) =>
        new(c.Id, c.Name, c.Goal, c.StartDate, c.EndDate, c.IsActive);

    public static MilestoneDto ToDto(this Milestone m) =>
        new(m.Id, m.Title, m.DueDate);

    public static ReleaseDto ToDto(this Release r) =>
        new(r.Id, r.Name, r.PlannedDate, r.ReleasedAt, r.ReleaseNotes, r.Environments);

    public static WorkItemCardDto ToCard(this WorkItem w, Dictionary<Guid, string> names) =>
        new(w.Id, w.Code, w.Title, w.Type,
            w.AssigneeId, w.AssigneeId.HasValue ? names.GetValueOrDefault(w.AssigneeId.Value) : null,
            w.Priority, w.StateId, w.State?.Name ?? string.Empty, w.State?.Color,
            w.DueDate, w.IsBlocked, ChecklistProgress(w), w.Labels,
            w.ProjectId, w.Project?.Code, Convert.ToBase64String(w.RowVersion));

    public static WorkItemDetailDto ToDetail(this WorkItem w, Dictionary<Guid, string> names) =>
        new(w.Id, w.Code, w.Title, w.Type,
            w.Description, w.Objective, w.Context,
            w.AuthorId, w.AssigneeId, w.AssigneeId.HasValue ? names.GetValueOrDefault(w.AssigneeId.Value) : null,
            w.Collaborators, w.Reviewers, w.Followers,
            w.Priority, w.StartDate, w.DueDate, w.EstimateHours,
            w.CycleId, w.EpicId, w.ReleaseId,
            w.StateId, w.State?.Name ?? string.Empty, w.Category,
            w.IsBlocked, w.BlockReason, w.BlockedByWorkItemId,
            w.AcceptanceCriteria, w.Labels,
            w.ReproSteps, w.ExpectedResult, w.ActualResult,
            w.Environment, w.AffectedVersion, w.Severity,
            w.ResearchQuestion, w.ResearchConclusion,
            w.ProjectId, w.Project?.Code, w.OrganizationId,
            w.CreatedAt, w.UpdatedAt,
            w.ParentTaskId, w.Notes, w.ProgressPercent, w.AccentColor,
            Convert.ToBase64String(w.RowVersion), w.Title, w.IsArchived);

    public static int? ChecklistProgress(WorkItem w)
    {
        var items = w.Checklists.SelectMany(c => c.Items).ToList();
        if (items.Count == 0) return null;
        return (int)Math.Round(100.0 * items.Count(i => i.IsCompleted) / items.Count);
    }

    public static ChecklistDto ToDto(this Checklist c) =>
        new(c.Id, c.Title, c.Order, c.Items.OrderBy(i => i.Order).Select(i => i.ToDto()).ToList());

    public static ChecklistItemDto ToDto(this ChecklistItem i) =>
        new(i.Id, i.Text, i.IsCompleted, i.AssigneeId, i.CompletedBy, i.Order);

    public static CommentDto ToDto(this WorkItemComment c, Dictionary<Guid, string> names) =>
        new(c.Id, c.AuthorId, names.GetValueOrDefault(c.AuthorId), c.Body, c.CreatedAt);

    public static TicketDto ToDto(this Ticket t, Dictionary<Guid, string> names) =>
        new(t.Id, t.Number, t.Code, t.Title,
            t.RequesterId, t.RequesterName, t.Origin, t.Category, t.Severity, t.Priority,
            t.ProjectId, t.Project?.Code, t.AssigneeId,
            t.Status, t.AffectedVersion, t.Environment, t.ResolvedAt, t.CreatedAt);

    public static TicketCommentDto ToDto(this TicketComment c, Dictionary<Guid, string> names) =>
        new(c.Id, c.AuthorId, names.GetValueOrDefault(c.AuthorId), c.Body, c.IsInternal, c.CreatedAt);

    public static DocumentDto ToDto(this Document d, bool includeContent = true, bool canEdit = false, bool canManageAccess = false) =>
        new(d.Id, d.Title, d.ParentId, d.AuthorId, d.Tags, d.UpdatedAt, includeContent ? d.Content : null,
            d.ProjectId, d.Visibility, d.Readers.Select(r => r.UserId).Order().ToList(), canEdit, canManageAccess,
            d.ContentFormat, Convert.ToBase64String(d.RowVersion));

    public static DocumentVersionDto ToDto(this DocumentVersion v) =>
        new(v.Id, v.Content, v.AuthorId, v.CreatedAt, v.Note, v.ContentFormat);

    public static ChannelDto ToDto(this Channel c, bool isMember, int? unread) =>
        new(c.Id, c.Name, c.Topic, c.Type, c.ProjectId, c.Project?.Code, isMember, unread);

    public static MessageDto ToDto(this Message m, Dictionary<Guid, string> names) =>
        new(m.Id, m.ChannelId, m.AuthorId, names.GetValueOrDefault(m.AuthorId), m.ThreadRootId, m.Body, m.IsEdited, m.CreatedAt);

    public static NotificationDto ToDto(this Notification n) =>
        new(n.Id, n.Event, n.Message, n.EntityType, n.EntityId, n.Route, n.IsRead, n.CreatedAt, n.SnoozedUntil);

    public static CalendarEventDto ToDto(this CalendarEvent e, Dictionary<Guid, string>? names = null) =>
        new(e.Id, e.Title, e.Description, e.Type, e.IsAllDay,
            e.IsAllDay ? CalendarDateRules.Civil(e.Start) : CalendarDateRules.Utc(e.Start),
            e.IsAllDay ? CalendarDateRules.Civil(e.End) : CalendarDateRules.Utc(e.End), e.TimeZoneId,
            e.Recurrence, e.LinkedWorkItemId, e.LinkedMilestoneId, e.LinkedReleaseId, e.ProjectId, e.Project?.Code,
            e.Priority, e.Color, e.AssigneeId,
            e.AssigneeId.HasValue ? names?.GetValueOrDefault(e.AssigneeId.Value) : null);
}
