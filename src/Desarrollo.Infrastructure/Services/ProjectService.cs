using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class ProjectService : IProjectService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public ProjectService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    {
        _db = db;
        _user = user;
        _permissions = permissions;
    }

    private Guid OrgId => _user.OrganizationId ?? throw new ForbiddenException();

    public async Task<Paged<ProjectDto>> ListAsync(ProjectListQuery query)
    {
        var q = _db.Projects.AsNoTracking().Where(p => p.OrganizationId == OrgId);
        if (!string.IsNullOrWhiteSpace(query.Search))
            q = q.Where(p => p.Name.Contains(query.Search) || p.Code.Contains(query.Search));
        if (query.Status.HasValue) q = q.Where(p => p.Status == query.Status.Value);

        var all = await q.OrderByDescending(p => p.UpdatedAt).ToListAsync();

        if (!await _permissions.IsOrganizationAdminAsync())
        {
            var accessible = new HashSet<Guid>();
            foreach (var p in all)
                if (await _permissions.CanAccessProjectAsync(p.Id))
                    accessible.Add(p.Id);
            all = all.Where(p => accessible.Contains(p.Id)).ToList();
        }

        var total = all.Count;
        var page = all.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToList();
        var dtos = page.Select(p => p.ToDto()).ToList();
        return new Paged<ProjectDto>(dtos, total, query.Page, query.PageSize);
    }

    public async Task<ProjectDetailDto> GetAsync(Guid id)
    {
        await _permissions.EnsureProjectPermissionAsync(id, Permission.Leer);
        var p = await _db.Projects.AsNoTracking()
            .Include(x => x.Members)
            .Include(x => x.Epics)
            .Include(x => x.Cycles)
            .Include(x => x.Milestones)
            .Include(x => x.Releases)
            .FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new NotFoundException($"Proyecto {id}");

        var roles = await _db.OrganizationRoles.AsNoTracking()
            .Where(r => r.OrganizationId == p.OrganizationId).ToDictionaryAsync(r => r.Id, r => r.Name);
        var userIds = p.Members.Select(m => m.UserId).ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Id, u => u.Email);

        var members = p.Members.Select(m => new MemberDto(m.Id, m.UserId, m.Name, m.IsActive,
            m.RoleId.HasValue ? roles.GetValueOrDefault(m.RoleId.Value) : null,
            emails.GetValueOrDefault(m.UserId), m.RoleId)).ToList();

        return new ProjectDetailDto(p.ToDto(), p.Scope, p.StartDate, p.Risks, p.Links, members,
            p.Epics.Select(e => e.ToDto()).ToList(),
            p.Cycles.Select(c => c.ToDto()).ToList(),
            p.Milestones.Select(m => m.ToDto()).ToList(),
            p.Releases.Select(r => r.ToDto()).ToList());
    }

    public async Task<ProjectDto> CreateAsync(CreateProjectRequest req)
    {
        if (!await _permissions.IsOrganizationAdminAsync())
            throw new ForbiddenException("Solo un administrador puede crear proyectos.");

        var project = new Project
        {
            OrganizationId = OrgId,
            Name = req.Name,
            Code = req.Code.Trim().ToUpperInvariant(),
            Objective = req.Objective,
            ProductId = req.ProductId,
            LeaderId = req.LeaderId,
            DueDate = req.DueDate,
            Priority = req.Priority,
            Status = ProjectStatus.Propuesto,
            CreatedBy = _user.UserId,
            UpdatedBy = _user.UserId,
        };

        _db.Projects.Add(project);
        await _db.EnsureDefaultStatesAsync(project.Id, OrgId);
        await _db.EnsureDefaultChannelsAsync(project.Id, OrgId);
        await _db.SaveChangesAsync();
        return project.ToDto();
    }

    public async Task UpdateAsync(Guid id, UpdateProjectRequest req)
    {
        await _permissions.EnsureProjectPermissionAsync(id, Permission.Editar);
        var p = await _db.Projects.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new NotFoundException($"Proyecto {id}");
        p.Name = req.Name;
        p.Objective = req.Objective;
        p.Scope = req.Scope;
        p.LeaderId = req.LeaderId;
        p.Status = req.Status;
        p.Priority = req.Priority;
        p.StartDate = req.StartDate;
        p.DueDate = req.DueDate;
        p.Risks = req.Risks;
        p.UpdatedBy = _user.UserId;
        p.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkflowStateDto>> GetStatesAsync(Guid projectId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        var states = await _db.WorkflowStates.AsNoTracking()
            .Where(s => s.ProjectId == projectId && !s.IsArchived)
            .OrderBy(s => s.Order).ToListAsync();
        return states.Select(s => s.ToDto()).ToList();
    }

    public async Task<List<EpicDto>> GetEpicsAsync(Guid projectId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        return (await _db.Epics.AsNoTracking().Where(e => e.ProjectId == projectId).ToListAsync())
            .Select(e => e.ToDto()).ToList();
    }

    public async Task<List<CycleDto>> GetCyclesAsync(Guid projectId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        return (await _db.Cycles.AsNoTracking().Where(c => c.ProjectId == projectId).ToListAsync())
            .Select(c => c.ToDto()).ToList();
    }

    public async Task<List<ReleaseDto>> GetReleasesAsync(Guid projectId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        return (await _db.Releases.AsNoTracking().Where(r => r.ProjectId == projectId).ToListAsync())
            .Select(r => r.ToDto()).ToList();
    }

    public async Task<List<MemberDto>> GetProjectMembersAsync(Guid projectId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        var members = await _db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId).ToListAsync();
        var roles = await _db.OrganizationRoles.AsNoTracking()
            .Where(r => r.OrganizationId == OrgId).ToDictionaryAsync(r => r.Id, r => r.Name);
        var userIds = members.Select(m => m.UserId).ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Id, u => u.Email);
        return members.Select(m => new MemberDto(m.Id, m.UserId, m.Name, m.IsActive,
            m.RoleId.HasValue ? roles.GetValueOrDefault(m.RoleId.Value) : null,
            emails.GetValueOrDefault(m.UserId), m.RoleId)).ToList();
    }

    public async Task<MemberDto> AddMemberAsync(Guid projectId, Guid userId, Guid? roleId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.AdministrarMiembros);
        var project = await _db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == projectId)
            ?? throw new NotFoundException($"Proyecto {projectId}");
        var orgMember = await _db.OrganizationMembers.AsNoTracking()
            .FirstOrDefaultAsync(m => m.OrganizationId == project.OrganizationId && m.UserId == userId && m.IsActive)
            ?? throw new ValidationException("El usuario no es miembro de la organización.");

        var existing = await _db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId);
        if (existing is not null)
        {
            existing.RoleId = roleId;
            existing.IsActive = true;
        }
        else
        {
            _db.ProjectMembers.Add(new ProjectMember
            {
                ProjectId = projectId,
                OrganizationMemberId = orgMember.Id,
                UserId = userId,
                RoleId = roleId,
                Name = orgMember.Name,
                IsActive = true,
                CreatedBy = _user.UserId,
            });
        }

        // Añade al miembro al canal general del proyecto para que el chat funcione.
        var channels = await _db.Channels.Where(c => c.ProjectId == projectId).Select(c => c.Id).ToListAsync();
        foreach (var ch in channels)
            if (!await _db.ChannelMembers.AnyAsync(cm => cm.ChannelId == ch && cm.UserId == userId))
                _db.ChannelMembers.Add(new ChannelMember { ChannelId = ch, UserId = userId });

        await _db.SaveChangesAsync();

        var roleName = roleId.HasValue
            ? await _db.OrganizationRoles.AsNoTracking().Where(r => r.Id == roleId.Value).Select(r => r.Name).FirstOrDefaultAsync()
            : null;
        return new MemberDto(existing?.Id ?? Guid.Empty, userId, orgMember.Name, true, roleName, null, roleId);
    }

    public async Task RemoveMemberAsync(Guid projectId, Guid userId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.AdministrarMiembros);
        var member = await _db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId);
        if (member is not null)
        {
            member.IsActive = false;
            await _db.SaveChangesAsync();
        }
    }

    public async Task UpdateMemberRoleAsync(Guid projectId, Guid userId, Guid? roleId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.AdministrarMiembros);
        var member = await _db.ProjectMembers.FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == userId)
            ?? throw new NotFoundException("Miembro no encontrado.");
        member.RoleId = roleId;
        await _db.SaveChangesAsync();
    }
}
