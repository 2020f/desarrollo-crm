using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Domain.Tickets;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public sealed record TicketAccessScope(Guid OrganizationId, Guid UserId, HashSet<Permission> OrganizationPermissions,
    IReadOnlyDictionary<Guid, HashSet<Permission>> ProjectPermissions);

/// <summary>One ticket audience policy for API data, notifications and contextual attachments.</summary>
public static class TicketAccessRules
{
    public static async Task<TicketAccessScope> LoadScopeAsync(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    {
        var organizationId = user.OrganizationId ?? throw new ForbiddenException();
        var userId = user.UserId ?? throw new ForbiddenException();
        var member = await db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == organizationId
            && m.UserId == userId && m.IsActive);
        if (member is null || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive))
            throw new ForbiddenException("La cuenta o la membresía ya no está activa.");
        var role = member.RoleId.HasValue ? await db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == member.RoleId && r.OrganizationId == organizationId) : null;
        var organizationPermissions = role?.Permissions.ToHashSet() ?? new HashSet<Permission>();
        var projects = await db.Projects.AsNoTracking().Where(p => p.OrganizationId == organizationId
            && p.Status != ProjectStatus.Archivado).Select(p => p.Id).ToListAsync();
        var projectPermissions = new Dictionary<Guid, HashSet<Permission>>();
        foreach (var projectId in projects)
        {
            var effective = await permissions.GetProjectPermissionsAsync(projectId);
            if (effective.Contains(Permission.Leer)) projectPermissions[projectId] = effective;
        }
        return new(organizationId, userId, organizationPermissions, projectPermissions);
    }

    public static IQueryable<Ticket> ReadableQuery(AppDbContext db, TicketAccessScope scope)
    {
        var projectIds = scope.ProjectPermissions.Keys.ToList();
        var personalRead = scope.OrganizationPermissions.Contains(Permission.Leer);
        return db.Tickets.AsNoTracking().Where(t => t.OrganizationId == scope.OrganizationId
            && ((t.ProjectId.HasValue && projectIds.Contains(t.ProjectId.Value))
                || (!t.ProjectId.HasValue && personalRead && (t.RequesterId == scope.UserId || t.AssigneeId == scope.UserId))));
    }

    public static HashSet<Permission> PermissionsFor(Ticket ticket, TicketAccessScope scope) => ticket.ProjectId.HasValue
        ? scope.ProjectPermissions.GetValueOrDefault(ticket.ProjectId.Value) ?? new HashSet<Permission>()
        : scope.OrganizationPermissions;

    public static void RequireRead(Ticket ticket, TicketAccessScope scope)
    {
        if (ticket.OrganizationId != scope.OrganizationId || !PermissionsFor(ticket, scope).Contains(Permission.Leer)
            || (!ticket.ProjectId.HasValue && ticket.RequesterId != scope.UserId && ticket.AssigneeId != scope.UserId))
            throw new ForbiddenException("No tienes acceso a esta solicitud.");
    }

    public static void Require(Ticket ticket, TicketAccessScope scope, Permission permission)
    {
        RequireRead(ticket, scope);
        if (!PermissionsFor(ticket, scope).Contains(permission)) throw new ForbiddenException($"Requiere el permiso '{permission}'.");
    }

    public static async Task ValidateAssigneeAsync(AppDbContext db, TicketAccessScope scope, Guid? projectId, Guid? assigneeId)
    {
        if (!assigneeId.HasValue) return;
        var member = await db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(m => m.OrganizationId == scope.OrganizationId
            && m.UserId == assigneeId && m.IsActive);
        if (member is null || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == assigneeId && u.IsActive))
            throw new ValidationException("La persona responsable debe ser un miembro activo de la organización.");
        var role = member.RoleId.HasValue ? await db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == member.RoleId && r.OrganizationId == scope.OrganizationId) : null;
        if (!projectId.HasValue)
        {
            if (role?.Permissions.Contains(Permission.Leer) == true) return;
            throw new ValidationException("La persona responsable no tiene permiso de lectura.");
        }
        var project = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId && p.OrganizationId == scope.OrganizationId
            && p.Status != ProjectStatus.Archivado) ?? throw new ForbiddenException();
        if (role is not null && Enum.GetValues<Permission>().All(role.Permissions.Contains)) return;
        var projectMember = await db.ProjectMembers.AsNoTracking().FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == assigneeId && m.IsActive);
        if (projectMember is null)
        {
            if (!project.IsPrivate) return;
        }
        else if (!projectMember.RoleId.HasValue) return;
        else if (await db.OrganizationRoles.AsNoTracking().AnyAsync(r => r.Id == projectMember.RoleId && r.OrganizationId == scope.OrganizationId
            && r.Permissions.Contains(Permission.Leer))) return;
        throw new ValidationException("La persona responsable no puede consultar este proyecto.");
    }
}
