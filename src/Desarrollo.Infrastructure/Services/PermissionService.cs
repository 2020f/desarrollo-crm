using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class PermissionService : IPermissionService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;

    public PermissionService(AppDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    private Guid UserId => _user.UserId ?? throw new ForbiddenException();

    public async Task<bool> IsOrganizationAdminAsync()
    {
        if (!await IsActiveMemberAsync()) return false;
        var orgId = _user.OrganizationId ?? throw new ForbiddenException();
        var member = await _db.OrganizationMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.UserId == _user.UserId && m.IsActive);
        if (member is null) return false;
        var perms = await GetRolePermissionsAsync(member.RoleId, null);
        return Enum.GetValues<Permission>().All(perms.Contains);
    }

    public async Task<bool> CanAccessProjectAsync(Guid projectId)
    {
        if (!await IsActiveMemberAsync()) return false;
        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null || project.OrganizationId != _user.OrganizationId) return false;
        if (await IsOrganizationAdminAsync()) return true;

        var pm = await _db.ProjectMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == _user.UserId && m.IsActive);
        if (pm is not null) return (await GetProjectPermissionsAsync(projectId)).Contains(Permission.Leer);

        if (!project.IsPrivate)
        {
            var om = await _db.OrganizationMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.OrganizationId == project.OrganizationId && m.UserId == _user.UserId && m.IsActive);
            return om is not null;
        }

        return false;
    }

    public async Task EnsureProjectPermissionAsync(Guid projectId, Permission permission)
    {
        var perms = await GetProjectPermissionsAsync(projectId);
        if (!perms.Contains(permission))
            throw new ForbiddenException($"Requiere el permiso '{permission}'.");
    }

    public async Task<HashSet<Permission>> GetProjectPermissionsAsync(Guid projectId)
    {
        var result = new HashSet<Permission>();
        if (!await IsActiveMemberAsync()) return result;

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == projectId);
        if (project is null) return result;
        if (_user.OrganizationId is null || project.OrganizationId != _user.OrganizationId.Value) return result;
        if (await IsOrganizationAdminAsync()) return Enum.GetValues<Permission>().ToHashSet();

        var pm = await _db.ProjectMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == _user.UserId && m.IsActive);
        if (pm is not null)
        {
            var perms = await GetRolePermissionsAsync(pm.RoleId, null);
            foreach (var p in perms) result.Add(p);
            if (!pm.RoleId.HasValue) result.UnionWith(new[] { Permission.Leer, Permission.Comentar });
            return result;
        }

        if (!project.IsPrivate)
        {
            var om = await _db.OrganizationMembers.AsNoTracking()
                .FirstOrDefaultAsync(m => m.OrganizationId == project.OrganizationId && m.UserId == _user.UserId && m.IsActive);
            if (om is not null)
            {
                result.UnionWith(new[] { Permission.Leer, Permission.Comentar });
            }
        }

        return result;
    }

    public async Task<bool> CanReadWorkItemAsync(Guid workItemId)
    {
        if (!await IsActiveMemberAsync()) return false;
        var wi = await _db.WorkItems.AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == workItemId);
        if (wi is null) return false;
        if (_user.OrganizationId is null || wi.OrganizationId != _user.OrganizationId.Value) return false;
        if (await IsOrganizationAdminAsync()) return true;
        if (wi.ProjectId is null) return wi.AuthorId == _user.UserId;
        return await CanAccessProjectAsync(wi.ProjectId.Value);
    }

    public async Task EnsureCanReadWorkItemAsync(Guid workItemId)
    {
        if (!await CanReadWorkItemAsync(workItemId))
            throw new ForbiddenException("No puede acceder a esta tarea.");
    }

    private async Task<HashSet<Permission>> GetRolePermissionsAsync(Guid? roleId, Guid? _)
    {
        if (roleId is null) return new HashSet<Permission>();
        var role = await _db.OrganizationRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == _user.OrganizationId);
        return role?.Permissions.ToHashSet() ?? new HashSet<Permission>();
    }

    private async Task<bool> IsActiveMemberAsync() => _user.UserId.HasValue && _user.OrganizationId.HasValue
        && await _db.Users.AsNoTracking().AnyAsync(u => u.Id == _user.UserId && u.IsActive)
        && await _db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == _user.OrganizationId
            && m.UserId == _user.UserId && m.IsActive);
}
