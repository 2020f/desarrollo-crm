using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class TaskDiagramService : ITaskDiagramService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    public TaskDiagramService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    { _db = db; _user = user; _permissions = permissions; }

    private Guid OrgId => _user.OrganizationId ?? throw new ForbiddenException();
    private Guid UserId => _user.UserId ?? throw new ForbiddenException();
    private static readonly string[] DefaultAccents = { "#A76BF3", "#4B9EFF", "#3AC5CA" };

    public async Task<TaskDiagramDto> GetAsync(Guid projectId, bool includeArchived = false)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        var project = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId && p.OrganizationId == OrgId);
        var query = _db.WorkItems.AsNoTracking().Where(w => w.ProjectId == projectId && w.OrganizationId == OrgId);
        if (!includeArchived) query = query.Where(w => !w.IsArchived);
        if (await query.CountAsync() > 10000) throw new ValidationException("Este mapa supera el límite de 10 000 tarjetas.");
        var tasks = await query.Include(w => w.State).Include(w => w.Checklists).ThenInclude(c => c.Items)
            .OrderBy(w => w.Sequence).ThenBy(w => w.Id).ToListAsync();
        var byId = tasks.ToDictionary(t => t.Id);
        var positions = await _db.TaskDiagramPositions.AsNoTracking().Where(p => p.ProjectId == projectId).ToDictionaryAsync(p => p.TaskId);
        var layout = TaskDiagramLayout.Positions(tasks, positions);
        var permissions = await _permissions.GetProjectPermissionsAsync(projectId);
        var names = await UserNames.ResolveAsync(_db, tasks.Select(t => t.AssigneeId));
        var dtos = tasks.Select(task =>
        {
            var (fullPath, depth) = WorkItemMutationRules.PathOf(task, byId);
            var displayTitle = task.ParentTaskId.HasValue && byId.TryGetValue(task.ParentTaskId.Value, out var parent)
                ? $"{parent.Title} · {task.Title}" : task.Title;
            var position = positions.GetValueOrDefault(task.Id);
            return new DiagramTaskDto(task.Id, task.Code, task.Title, displayTitle, task.ParentTaskId,
                task.Description, task.Notes, task.Priority, task.StateId, task.State?.Name ?? "", task.State?.Color,
                task.ProgressPercent, task.AccentColor ?? DefaultAccents[Math.Min(depth, DefaultAccents.Length - 1)],
                depth, layout[task.Id].X, layout[task.Id].Y, Convert.ToBase64String(task.RowVersion),
                position is null ? null : Convert.ToBase64String(position.RowVersion), Mappers.ChecklistProgress(task),
                fullPath, task.IsArchived, !task.IsArchived && permissions.Contains(Permission.Editar),
                !task.IsArchived && permissions.Contains(Permission.Crear), permissions.Contains(Permission.Archivar),
                !task.IsArchived && permissions.Contains(Permission.CambiarEstado), task.Type, task.AssigneeId,
                task.AssigneeId.HasValue ? names.GetValueOrDefault(task.AssigneeId.Value) : null);
        }).ToList();
        var view = await _db.TaskDiagramUserViews.AsNoTracking().FirstOrDefaultAsync(v => v.ProjectId == projectId && v.UserId == UserId);
        var states = await _db.WorkflowStates.AsNoTracking().Where(s => s.ProjectId == projectId && !s.IsArchived).OrderBy(s => s.Order).ToListAsync();
        return new TaskDiagramDto(dtos, view?.ViewportX ?? 0, view?.ViewportY ?? 0, view?.Zoom ?? 0,
            project.Name, permissions.Order().ToList(), states.Select(s => s.ToDto()).ToList(),
            view?.CollapsedTaskIds.Where(byId.ContainsKey).ToList() ?? new());
    }

    public async Task<List<DiagramPositionDto>> SavePositionsAsync(Guid projectId, List<DiagramPositionDto> positions)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Editar);
        if (positions is null || positions.Count > 1000) throw new ValidationException("Envía hasta 1000 posiciones por operación.");
        if (positions.Select(p => p.TaskId).Distinct().Count() != positions.Count) throw new ValidationException("No repitas una tarea en la misma operación.");
        foreach (var position in positions) ValidateCoordinates(position.X, position.Y, 1000000);
        if (positions.Count == 0) return new();
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Editar);
        var ids = positions.Select(p => p.TaskId).ToList();
        if (await _db.WorkItems.AsNoTracking().CountAsync(w => ids.Contains(w.Id) && w.ProjectId == projectId
            && w.OrganizationId == OrgId && !w.IsArchived) != ids.Count)
            throw new ForbiddenException("Alguna tarjeta no está disponible para editar en este proyecto.");
        var existing = await _db.TaskDiagramPositions.Where(p => p.ProjectId == projectId && ids.Contains(p.TaskId)).ToDictionaryAsync(p => p.TaskId);
        foreach (var position in positions)
        {
            if (existing.TryGetValue(position.TaskId, out var entity))
            {
                await _db.Entry(entity).ReloadAsync();
                if (position.RowVersion is null) throw new ConcurrencyConflictException("La posición ya cambió. Recupera el mapa actual antes de volver a moverla.");
                WorkItemMutationRules.RequireVersion(entity, position.RowVersion);
                entity.X = position.X; entity.Y = position.Y; entity.UpdatedAt = DateTime.UtcNow; entity.UpdatedBy = UserId;
            }
            else
            {
                if (position.RowVersion is not null) throw new ConcurrencyConflictException("La posición ya no coincide con el mapa actual.");
                entity = new TaskDiagramPosition { ProjectId = projectId, TaskId = position.TaskId, X = position.X, Y = position.Y, CreatedBy = UserId, UpdatedBy = UserId };
                _db.TaskDiagramPositions.Add(entity);
                existing.Add(position.TaskId, entity);
            }
        }
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
        await transaction.CommitAsync();
        return positions.Select(p => new DiagramPositionDto(p.TaskId, p.X, p.Y, Convert.ToBase64String(existing[p.TaskId].RowVersion))).ToList();
    }

    public async Task SaveViewportAsync(Guid projectId, SaveViewportRequest viewport)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        ValidateCoordinates(viewport.ViewportX, viewport.ViewportY, 10000000);
        if (!double.IsFinite(viewport.Zoom) || viewport.Zoom < 0.1 || viewport.Zoom > 4)
            throw new ValidationException("El zoom debe estar entre 10% y 400%.");
        if (viewport.CollapsedTaskIds is { Count: > 10000 }) throw new ValidationException("Demasiadas ramas contraídas.");
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);
        if (viewport.CollapsedTaskIds is not null)
        {
            var ids = viewport.CollapsedTaskIds.Distinct().ToList();
            if (await _db.WorkItems.AsNoTracking().CountAsync(w => w.ProjectId == projectId && w.OrganizationId == OrgId && ids.Contains(w.Id)) != ids.Count)
                throw new ValidationException("Las ramas contraídas deben pertenecer al proyecto.");
        }
        var view = await _db.TaskDiagramUserViews.FirstOrDefaultAsync(v => v.ProjectId == projectId && v.UserId == UserId);
        if (view is null)
        {
            view = new TaskDiagramUserView { ProjectId = projectId, UserId = UserId, CreatedBy = UserId };
            _db.TaskDiagramUserViews.Add(view);
        }
        else await _db.Entry(view).ReloadAsync();
        view.ViewportX = viewport.ViewportX; view.ViewportY = viewport.ViewportY; view.Zoom = viewport.Zoom;
        if (viewport.CollapsedTaskIds is not null) view.CollapsedTaskIds = viewport.CollapsedTaskIds.Distinct().ToList();
        view.UpdatedBy = UserId; view.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void ValidateCoordinates(double x, double y, double bound)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || Math.Abs(x) > bound || Math.Abs(y) > bound)
            throw new ValidationException("Las coordenadas no son válidas o exceden el tamaño del lienzo.");
    }
}
