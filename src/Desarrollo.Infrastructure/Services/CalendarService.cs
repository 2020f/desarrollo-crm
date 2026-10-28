using System.Security.Cryptography;
using System.Text;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Calendar;
using Desarrollo.Domain.Projects;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class CalendarService : ICalendarService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public CalendarService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    { _db = db; _user = user; _permissions = permissions; }

    private Guid OrgId => _user.OrganizationId ?? throw new ForbiddenException();
    private Guid UserId => _user.UserId ?? throw new ForbiddenException();

    public async Task<List<CalendarEventDto>> GetRangeAsync(DateTime start, DateTime end, string? timeZoneId = null)
    {
        await EnsureActiveMemberAsync();
        var (utcStart, utcEnd) = CalendarDateRules.Range(start, end);
        var profileZone = await _db.Users.AsNoTracking().Where(u => u.Id == UserId).Select(u => u.TimeZoneId).FirstAsync();
        var zone = CalendarDateRules.Zone(timeZoneId ?? profileZone);
        var localStart = TimeZoneInfo.ConvertTimeFromUtc(utcStart, zone);
        var localEnd = TimeZoneInfo.ConvertTimeFromUtc(utcEnd, zone);
        var projects = await _db.Projects.AsNoTracking()
            .Where(p => p.OrganizationId == OrgId && p.Status != ProjectStatus.Archivado).ToListAsync();
        var projectPermissions = new Dictionary<Guid, HashSet<Permission>>();
        foreach (var project in projects)
        {
            var permissions = await _permissions.GetProjectPermissionsAsync(project.Id);
            if (permissions.Contains(Permission.Leer)) projectPermissions[project.Id] = permissions;
        }
        var projectIds = projectPermissions.Keys.ToList();
        var isOrganizationAdmin = await _permissions.IsOrganizationAdminAsync();
        var visibleProjects = projects.Where(p => projectPermissions.ContainsKey(p.Id)).ToDictionary(p => p.Id);

        var candidates = await _db.CalendarEvents.AsNoTracking().Include(e => e.Project)
            .Where(e => e.OrganizationId == OrgId && !e.IsCancelled
                && ((e.ProjectId.HasValue && projectIds.Contains(e.ProjectId.Value))
                    || (!e.ProjectId.HasValue && (e.CreatedBy == UserId || e.AssigneeId == UserId)))
                && (e.IsAllDay ? e.Start < localEnd && e.End > localStart : e.Start < utcEnd && e.End > utcStart))
            .ToListAsync();
        var events = new List<CalendarEvent>();
        foreach (var candidate in candidates)
            if (await LinksAreReadableAsync(candidate, projectIds)) events.Add(candidate);

        // Planning values are projections, not CalendarEvent copies. The next GET observes
        // changes/removal on the original record without a worker or synchronization job.
        var projected = new List<CalendarEventDto>();
        void Add(string sourceType, Guid sourceId, string dateKind, string title, string? description,
            Guid? projectId, DateTime? first, DateTime? last, CalendarEventType type = CalendarEventType.Tarea,
            Priority? priority = null, string? color = null, Guid? assigneeId = null, bool lastIsInstant = false)
        {
            if (!first.HasValue && !last.HasValue) return;
            if (lastIsInstant && last.HasValue)
                last = TimeZoneInfo.ConvertTimeFromUtc(CalendarDateRules.Utc(last.Value), zone);
            var from = CalendarDateRules.Civil(first ?? last!.Value);
            var through = CalendarDateRules.Civil(last ?? first!.Value);
            if (through < from || through == DateTime.MaxValue.Date) return;
            var until = through.AddDays(1);
            if (from >= localEnd || until <= localStart) return;
            var route = sourceType switch
            {
                "WorkItem" => $"/tasks/{sourceId}",
                "Release" => "/releases",
                "Epic" => $"/projects/{projectId}/backlog",
                _ => $"/projects/{projectId}",
            };
            projected.Add(new CalendarEventDto(ProjectionId(sourceType, sourceId, dateKind), title, description,
                type, true, from, until, zone.Id, null,
                sourceType == "WorkItem" ? sourceId : null,
                sourceType == "Milestone" ? sourceId : null,
                sourceType == "Release" ? sourceId : null,
                projectId, projectId.HasValue ? visibleProjects.GetValueOrDefault(projectId.Value)?.Code : null,
                priority, color, assigneeId, null, sourceType, sourceId, dateKind, route,
                IsDerived: true, CanEdit: false, CanDelete: false));
        }

        var tasks = await _db.WorkItems.AsNoTracking()
            .Where(w => w.OrganizationId == OrgId && !w.IsArchived && w.Category != WorkItemCategory.Cancelado
                && ((w.ProjectId.HasValue && projectIds.Contains(w.ProjectId.Value))
                    || (!w.ProjectId.HasValue && (w.AuthorId == UserId || isOrganizationAdmin)))
                && (w.StartDate.HasValue || w.DueDate.HasValue))
            .ToListAsync();
        foreach (var task in tasks)
            Add("WorkItem", task.Id, PeriodKind(task.StartDate, task.DueDate), task.Title, task.Description,
                task.ProjectId, task.StartDate, task.DueDate, priority: task.Priority, color: task.AccentColor, assigneeId: task.AssigneeId);
        foreach (var project in visibleProjects.Values)
            Add("Project", project.Id, PeriodKind(project.StartDate, project.DueDate), project.Name, project.Objective,
                project.Id, project.StartDate, project.DueDate, priority: project.Priority, assigneeId: project.LeaderId);
        var epics = await _db.Epics.AsNoTracking().Where(e => projectIds.Contains(e.ProjectId) && e.DueDate.HasValue).ToListAsync();
        foreach (var epic in epics)
            Add("Epic", epic.Id, "Due", epic.Title, epic.Description, epic.ProjectId, null, epic.DueDate,
                priority: epic.Priority, assigneeId: epic.OwnerId);
        var cycles = await _db.Cycles.AsNoTracking().Where(c => projectIds.Contains(c.ProjectId) && c.IsActive).ToListAsync();
        foreach (var cycle in cycles)
            Add("Cycle", cycle.Id, "Period", cycle.Name, cycle.Goal, cycle.ProjectId, cycle.StartDate, cycle.EndDate);
        var milestones = await _db.Milestones.AsNoTracking().Where(m => projectIds.Contains(m.ProjectId) && m.DueDate.HasValue).ToListAsync();
        foreach (var milestone in milestones)
            Add("Milestone", milestone.Id, "Due", milestone.Title, milestone.Description, milestone.ProjectId,
                null, milestone.DueDate, CalendarEventType.Hito);
        var releases = await _db.Releases.AsNoTracking().Where(r => projectIds.Contains(r.ProjectId)
            && (r.PlannedDate.HasValue || r.ReleasedAt.HasValue)).ToListAsync();
        foreach (var release in releases)
        {
            if (release.PlannedDate.HasValue)
                Add("Release", release.Id, "Planned", release.Name, release.Scope, release.ProjectId,
                    null, release.PlannedDate, CalendarEventType.Publicacion);
            if (release.ReleasedAt.HasValue)
                Add("Release", release.Id, "Released", release.Name, release.ReleaseNotes, release.ProjectId,
                    null, release.ReleasedAt, CalendarEventType.Publicacion, lastIsInstant: true);
        }

        // A visible all-day manual event replaces only an exact interval of its explicit
        // linked source. A meeting or a differently dated reminder does not hide planning.
        projected.RemoveAll(p => events.Any(e => e.IsAllDay && CalendarDateRules.Civil(e.Start) == p.Start
            && CalendarDateRules.Civil(e.End) == p.End && Represents(e, p)));
        var names = await UserNames.ResolveAsync(_db, events.Select(e => e.AssigneeId).Concat(projected.Select(e => e.AssigneeId)));
        var result = events.Select(e => WithCapabilities(e, e.ToDto(names), projectPermissions)).ToList();
        result.AddRange(projected.Select(e => e with { AssigneeName = e.AssigneeId.HasValue ? names.GetValueOrDefault(e.AssigneeId.Value) : null }));
        return result.OrderBy(e => e.Start).ThenBy(e => e.Title).ThenBy(e => e.Id).ToList();
    }

    public async Task<CalendarEventDto> CreateAsync(CreateCalendarEventRequest req)
    {
        await EnsureActiveMemberAsync();
        var calendarEvent = new CalendarEvent { OrganizationId = OrgId, CreatedBy = UserId, UpdatedBy = UserId };
        await ApplyRequestAsync(calendarEvent, req, creating: true);
        _db.CalendarEvents.Add(calendarEvent);
        if (calendarEvent.AssigneeId.HasValue && calendarEvent.AssigneeId != UserId)
            await NotificationHelper.NotifyAsync(_db, OrgId, calendarEvent.AssigneeId.Value, NotificationEvent.Asignacion,
                $"Te invitaron al evento: {calendarEvent.Title}.", "CalendarEvent", calendarEvent.Id, "/calendar",
                $"cal:{calendarEvent.Id}:{calendarEvent.AssigneeId}");
        await _db.SaveChangesAsync();
        return await ManualDtoAsync(calendarEvent);
    }

    public async Task<CalendarEventDto> UpdateAsync(Guid id, CreateCalendarEventRequest req)
    {
        await EnsureActiveMemberAsync();
        var calendarEvent = await LoadForMutationAsync(id, Permission.Editar);
        await ApplyRequestAsync(calendarEvent, req, creating: false);
        calendarEvent.UpdatedBy = UserId; calendarEvent.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await ManualDtoAsync(calendarEvent);
    }

    public async Task DeleteAsync(Guid id)
    {
        await EnsureActiveMemberAsync();
        var calendarEvent = await LoadForMutationAsync(id, Permission.Eliminar);
        _db.CalendarEvents.Remove(calendarEvent);
        await _db.SaveChangesAsync();
    }

    private async Task EnsureActiveMemberAsync()
    {
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == UserId && u.IsActive)
            || !await _db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == OrgId && m.UserId == UserId && m.IsActive))
            throw new ForbiddenException("No tienes una membresía activa en esta organización.");
    }

    private async Task<Project> RequireProjectAsync(Guid projectId, Permission permission)
    {
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId
            && p.OrganizationId == OrgId && p.Status != ProjectStatus.Archivado) ?? throw new ForbiddenException();
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        await _permissions.EnsureProjectPermissionAsync(projectId, permission);
        return project;
    }

    private async Task<CalendarEvent> LoadForMutationAsync(Guid id, Permission permission)
    {
        var calendarEvent = await _db.CalendarEvents.Include(e => e.Project)
            .FirstOrDefaultAsync(e => e.Id == id && e.OrganizationId == OrgId) ?? throw new NotFoundException("Evento no encontrado.");
        var ids = new List<Guid>();
        if (calendarEvent.ProjectId.HasValue)
        {
            await RequireProjectAsync(calendarEvent.ProjectId.Value, permission);
            ids.Add(calendarEvent.ProjectId.Value);
        }
        else if (calendarEvent.CreatedBy != UserId) throw new ForbiddenException();
        if (!await LinksAreReadableAsync(calendarEvent, ids)) throw new ForbiddenException();
        return calendarEvent;
    }

    private async Task ApplyRequestAsync(CalendarEvent calendarEvent, CreateCalendarEventRequest req, bool creating)
    {
        Project? project = null;
        if (req.ProjectId.HasValue)
        {
            project = await RequireProjectAsync(req.ProjectId.Value, creating ? Permission.Crear : Permission.Editar);
            if (!creating && req.ProjectId != calendarEvent.ProjectId)
                await _permissions.EnsureProjectPermissionAsync(req.ProjectId.Value, Permission.Crear);
        }
        WorkItemMutationRules.EnumValue(req.Type, "Tipo de evento");
        if (req.Priority.HasValue) WorkItemMutationRules.EnumValue(req.Priority.Value, "Prioridad");
        if (req.Recurrence.HasValue) WorkItemMutationRules.EnumValue(req.Recurrence.Value, "Recurrencia");
        var title = WorkItemMutationRules.Title(req.Title);
        var description = WorkItemMutationRules.Text(req.Description, 20000, "La descripción");
        var color = WorkItemMutationRules.Accent(req.Color);
        var zone = CalendarDateRules.Zone(req.TimeZoneId ?? await _db.Users.AsNoTracking()
            .Where(u => u.Id == UserId).Select(u => u.TimeZoneId).FirstAsync());
        var start = req.IsAllDay ? CalendarDateRules.Civil(req.Start) : CalendarDateRules.Utc(req.Start);
        var end = req.IsAllDay ? CalendarDateRules.Civil(req.End) : CalendarDateRules.Utc(req.End);
        if (end <= start) throw new ValidationException("El fin del evento debe ser posterior a su inicio.");
        if (new[] { req.LinkedWorkItemId, req.LinkedMilestoneId, req.LinkedReleaseId }.Count(id => id.HasValue) > 1)
            throw new ValidationException("Un evento puede vincularse a un único registro de origen.");
        var proposed = new CalendarEvent { OrganizationId = OrgId, ProjectId = req.ProjectId,
            CreatedBy = calendarEvent.CreatedBy, AssigneeId = req.AssigneeId,
            LinkedWorkItemId = req.LinkedWorkItemId, LinkedMilestoneId = req.LinkedMilestoneId, LinkedReleaseId = req.LinkedReleaseId };
        if (!await LinksAreReadableAsync(proposed, req.ProjectId.HasValue ? new List<Guid> { req.ProjectId.Value } : new()))
            throw new ValidationException("El vínculo debe ser accesible, activo y pertenecer al mismo proyecto que el evento.");
        await ValidateAssigneeAsync(req.AssigneeId, project,
            calendarEvent.AssigneeId != req.AssigneeId || (req.AssigneeId.HasValue && calendarEvent.ProjectId != req.ProjectId));
        calendarEvent.Title = title; calendarEvent.Description = description; calendarEvent.Type = req.Type;
        calendarEvent.IsAllDay = req.IsAllDay; calendarEvent.Start = start; calendarEvent.End = end;
        calendarEvent.TimeZoneId = zone.Id; calendarEvent.Recurrence = req.Recurrence;
        calendarEvent.ProjectId = req.ProjectId;
        calendarEvent.Priority = req.Priority; calendarEvent.Color = color; calendarEvent.AssigneeId = req.AssigneeId;
        calendarEvent.LinkedWorkItemId = req.LinkedWorkItemId; calendarEvent.LinkedMilestoneId = req.LinkedMilestoneId;
        calendarEvent.LinkedReleaseId = req.LinkedReleaseId;
    }

    private async Task ValidateAssigneeAsync(Guid? assigneeId, Project? project, bool assignmentChanged)
    {
        if (project is not null && assignmentChanged) await _permissions.EnsureProjectPermissionAsync(project.Id, Permission.Asignar);
        if (!assigneeId.HasValue) return;
        var member = await _db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == OrgId && m.UserId == assigneeId && m.IsActive);
        if (member is null || !await _db.Users.AsNoTracking().AnyAsync(u => u.Id == assigneeId && u.IsActive))
            throw new ValidationException("La persona invitada debe ser un miembro activo de la organización.");
        if (project is null) return;
        var role = member.RoleId.HasValue ? await _db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == member.RoleId && r.OrganizationId == OrgId) : null;
        if (role is not null && Enum.GetValues<Permission>().All(role.Permissions.Contains)) return;
        var projectMember = await _db.ProjectMembers.AsNoTracking().FirstOrDefaultAsync(m => m.ProjectId == project.Id && m.UserId == assigneeId && m.IsActive);
        if (projectMember is null)
        {
            if (!project.IsPrivate) return;
        }
        else if (!projectMember.RoleId.HasValue) return;
        else if (await _db.OrganizationRoles.AsNoTracking().AnyAsync(r => r.Id == projectMember.RoleId
            && r.OrganizationId == OrgId && r.Permissions.Contains(Permission.Leer))) return;
        throw new ValidationException("La persona invitada no tiene acceso de lectura al proyecto.");
    }

    private async Task<bool> LinksAreReadableAsync(CalendarEvent calendarEvent, IReadOnlyCollection<Guid> projectIds)
    {
        if (calendarEvent.LinkedWorkItemId.HasValue)
        {
            var task = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == calendarEvent.LinkedWorkItemId
                && w.OrganizationId == OrgId && !w.IsArchived && w.Category != WorkItemCategory.Cancelado);
            if (task is null || task.ProjectId != calendarEvent.ProjectId) return false;
            if (task.ProjectId.HasValue)
            {
                if (!projectIds.Contains(task.ProjectId.Value)) return false;
            }
            else if (task.AuthorId != UserId || task.AuthorId != calendarEvent.CreatedBy
                || (calendarEvent.AssigneeId.HasValue && calendarEvent.AssigneeId != task.AuthorId)) return false;
        }
        if (calendarEvent.LinkedMilestoneId.HasValue && (!calendarEvent.ProjectId.HasValue
            || !projectIds.Contains(calendarEvent.ProjectId.Value)
            || !await _db.Milestones.AsNoTracking().AnyAsync(m => m.Id == calendarEvent.LinkedMilestoneId && m.ProjectId == calendarEvent.ProjectId))) return false;
        if (calendarEvent.LinkedReleaseId.HasValue && (!calendarEvent.ProjectId.HasValue
            || !projectIds.Contains(calendarEvent.ProjectId.Value)
            || !await _db.Releases.AsNoTracking().AnyAsync(r => r.Id == calendarEvent.LinkedReleaseId && r.ProjectId == calendarEvent.ProjectId))) return false;
        return true;
    }

    private async Task<CalendarEventDto> ManualDtoAsync(CalendarEvent calendarEvent)
    {
        calendarEvent = await _db.CalendarEvents.AsNoTracking().Include(e => e.Project).FirstAsync(e => e.Id == calendarEvent.Id);
        var names = await UserNames.ResolveAsync(_db, new[] { calendarEvent.AssigneeId });
        var permissions = new Dictionary<Guid, HashSet<Permission>>();
        if (calendarEvent.ProjectId.HasValue)
            permissions[calendarEvent.ProjectId.Value] = await _permissions.GetProjectPermissionsAsync(calendarEvent.ProjectId.Value);
        return WithCapabilities(calendarEvent, calendarEvent.ToDto(names), permissions);
    }

    private CalendarEventDto WithCapabilities(CalendarEvent calendarEvent, CalendarEventDto dto,
        IReadOnlyDictionary<Guid, HashSet<Permission>> projectPermissions) => dto with
    {
        SourceId = calendarEvent.Id, SourceRoute = "/calendar",
        CanEdit = calendarEvent.ProjectId.HasValue
            ? projectPermissions.TryGetValue(calendarEvent.ProjectId.Value, out var edit) && edit.Contains(Permission.Editar)
            : calendarEvent.CreatedBy == UserId,
        CanDelete = calendarEvent.ProjectId.HasValue
            ? projectPermissions.TryGetValue(calendarEvent.ProjectId.Value, out var delete) && delete.Contains(Permission.Eliminar)
            : calendarEvent.CreatedBy == UserId,
    };

    private static bool Represents(CalendarEvent calendarEvent, CalendarEventDto projection) => projection.SourceType switch
    {
        "WorkItem" => calendarEvent.LinkedWorkItemId == projection.SourceId,
        "Milestone" => calendarEvent.LinkedMilestoneId == projection.SourceId,
        "Release" => calendarEvent.LinkedReleaseId == projection.SourceId,
        _ => false,
    };

    private static string PeriodKind(DateTime? start, DateTime? end) => start.HasValue && end.HasValue ? "Period" : start.HasValue ? "Start" : "Due";

    private static Guid ProjectionId(string sourceType, Guid sourceId, string dateKind) =>
        new(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceType}:{sourceId:N}:{dateKind}")).AsSpan(0, 16));
}
