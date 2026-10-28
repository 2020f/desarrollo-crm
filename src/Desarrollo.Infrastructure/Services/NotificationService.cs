using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Docs;
using Desarrollo.Domain.Notifications;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public NotificationService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    {
        _db = db;
        _user = user;
        _permissions = permissions;
    }

    private Guid UserId => _user.UserId ?? throw new ForbiddenException();

    public async Task<Paged<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize)
    {
        var q = await AuthorizedQueryAsync();
        if (unreadOnly) q = q.Where(n => !n.IsRead);
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(n => n.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new Paged<NotificationDto>(rows.Select(n => n.ToDto()).ToList(), total, page, pageSize);
    }

    public async Task<int> UnreadCountAsync() => await (await AuthorizedQueryAsync()).CountAsync(n => !n.IsRead);

    private async Task<IQueryable<Notification>> AuthorizedQueryAsync()
    {
        var orgId = _user.OrganizationId ?? throw new ForbiddenException();
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == UserId && u.IsActive)
            || !await _db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == orgId && m.UserId == UserId && m.IsActive))
            return _db.Notifications.AsNoTracking().Where(n => false);
        var projectIds = await _db.Projects.AsNoTracking().Where(p => p.OrganizationId == orgId).Select(p => p.Id).ToListAsync();
        var visible = new List<Guid>();
        foreach (var projectId in projectIds)
            if (await _permissions.CanAccessProjectAsync(projectId)) visible.Add(projectId);
        var isAdmin = await _permissions.IsOrganizationAdminAsync();
        var tasks = _db.WorkItems.Where(w => w.OrganizationId == orgId &&
            ((w.ProjectId.HasValue && visible.Contains(w.ProjectId.Value)) || (!w.ProjectId.HasValue && (w.AuthorId == UserId || isAdmin))));
        var calendarProjects = _db.Projects.Where(p => p.OrganizationId == orgId && p.Status != ProjectStatus.Archivado && visible.Contains(p.Id));
        var calendarTasks = tasks.Where(w => !w.IsArchived && w.Category != WorkItemCategory.Cancelado);
        var calendarEvents = _db.CalendarEvents.Where(e => e.OrganizationId == orgId && !e.IsCancelled
            && ((e.ProjectId.HasValue && calendarProjects.Any(p => p.Id == e.ProjectId))
                || (!e.ProjectId.HasValue && (e.CreatedBy == UserId || e.AssigneeId == UserId)))
            && (!e.LinkedWorkItemId.HasValue || calendarTasks.Any(w => w.Id == e.LinkedWorkItemId
                && w.ProjectId == e.ProjectId
                && (w.ProjectId.HasValue || (w.AuthorId == UserId && w.AuthorId == e.CreatedBy
                    && (!e.AssigneeId.HasValue || e.AssigneeId == w.AuthorId)))))
            && (!e.LinkedMilestoneId.HasValue || (e.ProjectId.HasValue
                && _db.Milestones.Any(m => m.Id == e.LinkedMilestoneId && m.ProjectId == e.ProjectId)))
            && (!e.LinkedReleaseId.HasValue || (e.ProjectId.HasValue
                && _db.Releases.Any(r => r.Id == e.LinkedReleaseId && r.ProjectId == e.ProjectId))));
        var documents = _db.Documents.Where(d => d.OrganizationId == orgId && !d.IsArchived
            && calendarProjects.Any(p => p.Id == d.ProjectId)
            && (d.AuthorId == UserId || d.Visibility == DocumentVisibility.PublicaProyecto
                || (d.Visibility == DocumentVisibility.PersonasSeleccionadas && d.Readers.Any(r => r.UserId == UserId))));
        // Resolve only referenced document IDs separately. Adding its reader ACL to the
        // already nested calendar/task query can produce an excessive SQL memory grant.
        var documentIds = await documents.Where(d => _db.Notifications.Any(n => n.UserId == UserId
            && n.OrganizationId == orgId && n.EntityType == "Document" && n.EntityId == d.Id))
            .Select(d => d.Id).ToListAsync();
        var ticketIds = new List<Guid>();
        var ticketCommentIds = new List<Guid>();
        if (await _db.Notifications.AnyAsync(n => n.UserId == UserId && n.OrganizationId == orgId
            && (n.EntityType == "Ticket" || n.EntityType == "TicketComment")))
        {
            var ticketScope = await TicketAccessRules.LoadScopeAsync(_db, _user, _permissions);
            var readableTickets = TicketAccessRules.ReadableQuery(_db, ticketScope);
            ticketIds = await readableTickets.Where(t => _db.Notifications.Any(n => n.UserId == UserId
                && n.OrganizationId == orgId && n.EntityType == "Ticket" && n.EntityId == t.Id)).Select(t => t.Id).ToListAsync();
            var editProjects = ticketScope.ProjectPermissions.Where(p => p.Value.Contains(Permission.Editar)).Select(p => p.Key).ToList();
            var personalInternal = ticketScope.OrganizationPermissions.Contains(Permission.Editar);
            ticketCommentIds = await _db.TicketComments.Where(c => !c.IsDeleted
                && readableTickets.Any(t => t.Id == c.TicketId && (!c.IsInternal
                    || (t.ProjectId.HasValue ? editProjects.Contains(t.ProjectId.Value) : personalInternal)))
                && _db.Notifications.Any(n => n.UserId == UserId && n.OrganizationId == orgId
                    && n.EntityType == "TicketComment" && n.EntityId == c.Id))
                .Select(c => c.Id).ToListAsync();
        }
        var channelIds = new List<Guid>();
        var messageIds = new List<Guid>();
        if (await _db.Notifications.AnyAsync(n => n.UserId == UserId && n.OrganizationId == orgId
            && (n.EntityType == "Channel" || n.EntityType == "Message")))
        {
            channelIds = (await new ChannelService(_db, _user, _permissions).ListAsync(null)).Select(c => c.Id).ToList();
            messageIds = await _db.Messages.Where(m => !m.IsDeleted && channelIds.Contains(m.ChannelId)
                && (!m.ThreadRootId.HasValue || _db.Messages.Any(root => root.Id == m.ThreadRootId
                    && root.ChannelId == m.ChannelId && !root.IsDeleted && !root.ThreadRootId.HasValue))
                && _db.Notifications.Any(n => n.UserId == UserId && n.OrganizationId == orgId
                    && n.EntityType == "Message" && n.EntityId == m.Id)).Select(m => m.Id).ToListAsync();
        }
        return _db.Notifications.AsNoTracking().Where(n => n.UserId == UserId && n.OrganizationId == orgId
            && (n.EntityType != "WorkItem" || tasks.Any(w => w.Id == n.EntityId))
            && (n.EntityType != "CalendarEvent" || calendarEvents.Any(e => e.Id == n.EntityId))
            && (n.EntityType != "Document" || documentIds.Contains(n.EntityId))
            && (n.EntityType != "Ticket" || ticketIds.Contains(n.EntityId))
            && (n.EntityType != "TicketComment" || ticketCommentIds.Contains(n.EntityId))
            && (n.EntityType != "Channel" || channelIds.Contains(n.EntityId))
            && (n.EntityType != "Message" || messageIds.Contains(n.EntityId)));
    }

    public async Task MarkReadAsync(Guid id)
    {
        var n = await _db.Notifications.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId);
        if (n is null) return;
        n.IsRead = true;
        n.ReadAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task MarkAllReadAsync()
    {
        await _db.Notifications.Where(n => n.UserId == UserId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true).SetProperty(n => n.ReadAt, DateTime.UtcNow));
    }
}
