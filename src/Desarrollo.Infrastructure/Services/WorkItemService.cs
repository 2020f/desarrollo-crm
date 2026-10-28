using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Integrations;
using Desarrollo.Domain.Projects;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class WorkItemService : IWorkItemService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public WorkItemService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    {
        _db = db;
        _user = user;
        _permissions = permissions;
    }

    private Guid OrgId => _user.OrganizationId ?? throw new ForbiddenException();
    private Guid UserId => _user.UserId ?? throw new ForbiddenException();

    private async Task<IQueryable<WorkItem>> BaseQueryAsync()
    {
        var q = _db.WorkItems.AsNoTracking().Include(w => w.State).Include(w => w.Project)
            .Include(w => w.Checklists).ThenInclude(c => c.Items)
            .Where(w => w.OrganizationId == OrgId);

        if (!await _permissions.IsOrganizationAdminAsync())
        {
            var projects = await _db.Projects.AsNoTracking().Where(p => p.OrganizationId == OrgId).ToListAsync();
            var accessible = new List<Guid>();
            foreach (var p in projects)
                if (await _permissions.CanAccessProjectAsync(p.Id))
                    accessible.Add(p.Id);
            q = q.Where(w => (w.ProjectId == null && w.AuthorId == UserId) || accessible.Contains(w.ProjectId!.Value));
        }
        return q;
    }

    public async Task<Paged<WorkItemCardDto>> QueryAsync(WorkItemQuery query)
    {
        if (query.Page < 1 || query.PageSize < 1 || query.PageSize > 1000) throw new ValidationException("Paginación no válida.");
        if (query.ProjectId.HasValue) await _permissions.EnsureProjectPermissionAsync(query.ProjectId.Value, Permission.Leer);
        var q = await BaseQueryAsync();
        if (!query.IncludeArchived) q = q.Where(w => !w.IsArchived);

        if (query.ProjectId.HasValue) q = q.Where(w => w.ProjectId == query.ProjectId.Value);
        if (!string.IsNullOrWhiteSpace(query.Search)) q = q.Where(w => w.Title.Contains(query.Search) || w.Code.Contains(query.Search));
        if (query.AssigneeId.HasValue) q = q.Where(w => w.AssigneeId == query.AssigneeId.Value);
        if (query.Priority.HasValue) q = q.Where(w => w.Priority == query.Priority.Value);
        if (query.Type.HasValue) q = q.Where(w => w.Type == query.Type.Value);
        if (query.StateId.HasValue) q = q.Where(w => w.StateId == query.StateId.Value);
        if (query.Blocked.HasValue) q = q.Where(w => w.IsBlocked == query.Blocked.Value);
        if (query.EpicId.HasValue) q = q.Where(w => w.EpicId == query.EpicId.Value);
        if (query.CycleId.HasValue) q = q.Where(w => w.CycleId == query.CycleId.Value);
        if (query.Overdue == true) q = q.Where(w => w.DueDate.HasValue && w.DueDate.Value < DateTime.UtcNow && w.Category != WorkItemCategory.Finalizado && w.Category != WorkItemCategory.Cancelado);
        if (!string.IsNullOrWhiteSpace(query.Label)) q = q.Where(w => w.Labels.Contains(query.Label!));

        q = query.Sort switch
        {
            "priority" => q.OrderBy(w => w.Priority).ThenBy(w => w.UpdatedAt),
            "due" => q.OrderBy(w => w.DueDate),
            "created" => q.OrderByDescending(w => w.CreatedAt),
            _ => q.OrderBy(w => w.Code),
        };

        var total = await q.CountAsync();
        var rows = await q.Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();

        var userIds = rows.SelectMany(w => new[] { w.AssigneeId }).ToList();
        var names = await UserNames.ResolveAsync(_db, userIds);
        var cards = rows.Select(w => w.ToCard(names)).ToList();
        return new Paged<WorkItemCardDto>(cards, total, query.Page, query.PageSize);
    }


    public async Task<WorkItemDetailDto> GetAsync(Guid id)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var item = await _db.WorkItems.AsNoTracking().Include(w => w.State).Include(w => w.Project)
            .FirstOrDefaultAsync(w => w.Id == id && w.OrganizationId == OrgId) ?? throw new NotFoundException("Tarea no encontrada.");
        var names = await UserNames.ResolveAsync(_db, new[] { item.AssigneeId });
        var items = await _db.WorkItems.AsNoTracking().Where(w => w.ProjectId == item.ProjectId && w.OrganizationId == OrgId).ToDictionaryAsync(w => w.Id);
        var permissions = item.ProjectId.HasValue ? await _permissions.GetProjectPermissionsAsync(item.ProjectId.Value) : new HashSet<Permission>();
        return item.ToDetail(names) with
        {
            FullPath = WorkItemMutationRules.PathOf(item, items).FullPath, Permissions = permissions.Order().ToList(),
            CanEdit = !item.IsArchived && permissions.Contains(Permission.Editar),
            CanCreateChild = !item.IsArchived && permissions.Contains(Permission.Crear),
            CanArchive = permissions.Contains(Permission.Archivar),
            CanChangeState = !item.IsArchived && permissions.Contains(Permission.CambiarEstado),
        };
    }

    public Task<WorkItemDetailDto> CreateAsync(CreateWorkItemRequest req) => CreateCoreAsync(req);

    public async Task<WorkItemDetailDto> CreateChildAsync(Guid parentId, CreateWorkItemRequest req)
    {
        await _permissions.EnsureCanReadWorkItemAsync(parentId);
        var parent = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == parentId && w.OrganizationId == OrgId)
            ?? throw new NotFoundException("Tarea madre no encontrada.");
        if (!parent.ProjectId.HasValue) throw new ValidationException("La tarea madre debe pertenecer a un proyecto.");
        if (req.ProjectId.HasValue && req.ProjectId != parent.ProjectId)
            throw new ValidationException("La hija debe pertenecer al proyecto de su madre.");
        if (req.ParentTaskId.HasValue && req.ParentTaskId != parentId)
            throw new ValidationException("La tarea madre no coincide con la ruta.");
        return await CreateCoreAsync(req with { ProjectId = parent.ProjectId, ParentTaskId = parentId });
    }

    private async Task<WorkItemDetailDto> CreateCoreAsync(CreateWorkItemRequest req)
    {
        if (!req.ProjectId.HasValue) throw new ValidationException("Debe indicar un proyecto.");
        var projectId = req.ProjectId.Value;
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Crear);
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Crear);
        if (req.ClientRequestId == Guid.Empty) throw new ValidationException("El identificador de creación no es válido.");
        if (req.ClientRequestId.HasValue)
        {
            var existing = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.OrganizationId == OrgId
                && w.AuthorId == UserId && w.CreationRequestId == req.ClientRequestId);
            if (existing is not null)
            {
                if (existing.ProjectId != projectId || existing.ParentTaskId != req.ParentTaskId)
                    throw new InvalidStateException("El identificador de creación ya se usó para otra operación.");
                await transaction.CommitAsync();
                return await GetAsync(existing.Id);
            }
        }
        WorkItemMutationRules.EnumValue(req.Type, "Tipo");
        WorkItemMutationRules.EnumValue(req.Priority, "Prioridad");
        WorkItemMutationRules.Progress(req.ProgressPercent);
        await WorkItemMutationRules.ValidateParentAsync(_db, _permissions, projectId, OrgId, null, req.ParentTaskId);
        await WorkItemMutationRules.ValidateAssigneeAsync(_db, _permissions, projectId, OrgId, req.AssigneeId, null);
        var defaultStateId = await ResolveDefaultStateAsync(projectId);
        var initialState = await WorkItemMutationRules.StateAsync(_db, projectId, defaultStateId);
        var state = await WorkItemMutationRules.StateAsync(_db, projectId, req.StateId ?? defaultStateId);
        if (state.Category == WorkItemCategory.Finalizado) await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Aprobar);
        var project = await _db.Projects.FirstAsync(p => p.Id == projectId && p.OrganizationId == OrgId);
        await _db.Entry(project).ReloadAsync();
        var item = new WorkItem
        {
            OrganizationId = OrgId, ProjectId = projectId, ProductId = project.ProductId,
            Sequence = project.WorkItemSequence, Code = $"{project.Code}-{project.WorkItemSequence:D2}",
            Type = req.Type, Title = WorkItemMutationRules.Title(req.Title),
            Description = WorkItemMutationRules.Text(req.Description, 20000, "La descripción"),
            AssigneeId = req.AssigneeId, Priority = req.Priority, DueDate = req.DueDate,
            StateId = initialState.Id, Category = initialState.Category, ParentTaskId = req.ParentTaskId,
            Notes = WorkItemMutationRules.Text(req.Notes, 4000, "La nota"), ProgressPercent = req.ProgressPercent,
            AccentColor = WorkItemMutationRules.Accent(req.AccentColor), CreationRequestId = req.ClientRequestId,
            AuthorId = UserId, CreatedBy = UserId, UpdatedBy = UserId,
        };
        await WorkItemMutationRules.ValidateTransitionAsync(_db, _permissions, item, state.Id);
        item.StateId = state.Id; item.Category = state.Category;
        item.Followers.Add(UserId);
        if (req.AssigneeId.HasValue && req.AssigneeId != UserId) item.Followers.Add(req.AssigneeId.Value);
        var (x, y) = await SuggestPositionAsync(projectId, req.ParentTaskId);
        project.WorkItemSequence++;
        project.UpdatedAt = DateTime.UtcNow;
        project.UpdatedBy = UserId;
        _db.WorkItems.Add(item);
        // Set the FK navigation so EF orders the new task before its position in the same transaction.
        _db.TaskDiagramPositions.Add(new TaskDiagramPosition { ProjectId = projectId, TaskId = item.Id, Task = item,
            X = x, Y = y, CreatedBy = UserId, UpdatedBy = UserId });
        if (req.ParentTaskId.HasValue) await TouchAncestorsAsync(req.ParentTaskId);
        Audit(item, "Create", null);
        await NotifyAssignmentAsync(item, null);
        await SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(item.Id);
    }

    private async Task<(double X, double Y)> SuggestPositionAsync(Guid projectId, Guid? parentId)
    {
        var tasks = await _db.WorkItems.AsNoTracking().Where(w => w.ProjectId == projectId && w.OrganizationId == OrgId && !w.IsArchived).ToListAsync();
        var saved = await _db.TaskDiagramPositions.AsNoTracking().Where(p => p.ProjectId == projectId).ToDictionaryAsync(p => p.TaskId);
        var positions = TaskDiagramLayout.Positions(tasks, saved);
        (double X, double Y) candidate;
        if (parentId.HasValue && positions.TryGetValue(parentId.Value, out var parent))
        {
            var siblings = tasks.Where(t => t.ParentTaskId == parentId).Select(t => positions[t.Id].Y).ToList();
            candidate = (parent.X + TaskDiagramLayout.ColumnGap, siblings.Count > 0 ? siblings.Max() + TaskDiagramLayout.RowGap : parent.Y);
        }
        else candidate = (40, positions.Count > 0 ? positions.Values.Max(p => p.Y) + TaskDiagramLayout.RowGap : 40);
        while (TaskDiagramLayout.Overlaps(candidate, positions.Values)) candidate.Y += TaskDiagramLayout.RowGap;
        return candidate;
    }

    public async Task<WorkItemDetailDto> ChangeParentAsync(Guid id, Guid? parentId, string? rowVersion = null)
    {
        var projectId = await RequireMutationProjectAsync(id, Permission.Editar);
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Editar);
        var item = await LoadMutationAsync(id, rowVersion, allowArchived: true);
        await WorkItemMutationRules.ValidateParentAsync(_db, _permissions, projectId, OrgId, id, parentId);
        var before = Snapshot(item);
        var previousParent = item.ParentTaskId;
        item.ParentTaskId = parentId;
        Touch(item);
        await TouchAncestorsAsync(previousParent);
        await TouchAncestorsAsync(parentId);
        Audit(item, "ChangeParent", before);
        await SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(id);
    }

    public async Task<WorkItemDetailDto> UpdateAsync(Guid id, UpdateWorkItemRequest req)
    {
        var projectId = await RequireMutationProjectAsync(id, Permission.Editar);
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Editar);
        var item = await LoadMutationAsync(id, req.RowVersion);
        var before = Snapshot(item);
        var oldAssignee = item.AssigneeId;
        WorkItemMutationRules.EnumValue(req.Type, "Tipo");
        WorkItemMutationRules.EnumValue(req.Priority, "Prioridad");
        WorkItemMutationRules.Progress(req.ProgressPercent);
        await WorkItemMutationRules.ValidateAssigneeAsync(_db, _permissions, projectId, OrgId, req.AssigneeId, oldAssignee);
        await ValidateRelatedFieldsAsync(projectId, req.CycleId, req.EpicId, req.ReleaseId);
        if (req.ParentTaskId.HasValue && req.ParentTaskId != item.ParentTaskId)
        {
            await WorkItemMutationRules.ValidateParentAsync(_db, _permissions, projectId, OrgId, id, req.ParentTaskId);
            await TouchAncestorsAsync(item.ParentTaskId);
            await TouchAncestorsAsync(req.ParentTaskId);
            item.ParentTaskId = req.ParentTaskId;
        }
        item.Title = WorkItemMutationRules.Title(req.Title);
        item.Type = req.Type;
        item.AssigneeId = req.AssigneeId;
        item.Priority = req.Priority;
        item.Description = WorkItemMutationRules.Text(req.Description, 20000, "La descripción");
        item.Objective = WorkItemMutationRules.Text(req.Objective, 20000, "El objetivo");
        item.Context = WorkItemMutationRules.Text(req.Context, 20000, "El contexto");
        if (req.StartDate.HasValue && req.DueDate.HasValue && req.StartDate > req.DueDate)
            throw new ValidationException("El inicio no puede ser posterior al vencimiento.");
        if (req.EstimateHours is < 0 or > 100000 || (req.EstimateHours.HasValue && !double.IsFinite(req.EstimateHours.Value)))
            throw new ValidationException("La estimación no es válida.");
        item.StartDate = req.StartDate; item.DueDate = req.DueDate; item.EstimateHours = req.EstimateHours;
        item.CycleId = req.CycleId; item.EpicId = req.EpicId; item.ReleaseId = req.ReleaseId;
        item.AcceptanceCriteria = WorkItemMutationRules.Text(req.AcceptanceCriteria, 20000, "Los criterios de aceptación");
        if (req.Labels is null || req.Labels.Count > 50 || req.Labels.Any(l => string.IsNullOrWhiteSpace(l) || l.Length > 100))
            throw new ValidationException("Las etiquetas no son válidas.");
        item.Labels = req.Labels.Distinct().ToList();
        item.ReproSteps = WorkItemMutationRules.Text(req.ReproSteps, 20000, "La reproducción");
        item.ExpectedResult = WorkItemMutationRules.Text(req.ExpectedResult, 20000, "El resultado esperado");
        item.ActualResult = WorkItemMutationRules.Text(req.ActualResult, 20000, "El resultado observado");
        item.Environment = WorkItemMutationRules.Text(req.Environment, 1000, "El ambiente");
        item.AffectedVersion = WorkItemMutationRules.Text(req.AffectedVersion, 200, "La versión");
        if (req.Severity.HasValue) WorkItemMutationRules.EnumValue(req.Severity.Value, "Severidad");
        item.Severity = req.Severity;
        item.ResearchQuestion = WorkItemMutationRules.Text(req.ResearchQuestion, 20000, "La pregunta");
        item.ResearchConclusion = WorkItemMutationRules.Text(req.ResearchConclusion, 20000, "La conclusión");
        // Optional map fields are preserved for older full-edit clients that do not submit them.
        if (req.Notes is not null) item.Notes = WorkItemMutationRules.Text(req.Notes, 4000, "La nota");
        if (req.ClearProgressPercent) item.ProgressPercent = null;
        else if (req.ProgressPercent.HasValue) item.ProgressPercent = req.ProgressPercent;
        if (req.AccentColor is not null) item.AccentColor = WorkItemMutationRules.Accent(req.AccentColor);
        await WorkItemMutationRules.ValidateTransitionAsync(_db, _permissions, item, req.StateId);
        var state = await WorkItemMutationRules.StateAsync(_db, projectId, req.StateId);
        item.StateId = state.Id; item.Category = state.Category;
        Touch(item);
        Audit(item, "Update", before);
        await NotifyAssignmentAsync(item, oldAssignee);
        await SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(id);
    }

    public async Task<WorkItemDetailDto> PatchAsync(Guid id, PatchWorkItemRequest req)
    {
        var projectId = await RequireMutationProjectAsync(id, Permission.Editar);
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Editar);
        var item = await LoadMutationAsync(id, req.RowVersion);
        var before = Snapshot(item);
        if (req.Title is not null) item.Title = WorkItemMutationRules.Title(req.Title);
        if (req.Description is not null) item.Description = WorkItemMutationRules.Text(req.Description, 20000, "La descripción");
        if (req.Notes is not null) item.Notes = WorkItemMutationRules.Text(req.Notes, 4000, "La nota");
        if (req.Priority.HasValue) { WorkItemMutationRules.EnumValue(req.Priority.Value, "Prioridad"); item.Priority = req.Priority.Value; }
        WorkItemMutationRules.Progress(req.ProgressPercent);
        if (req.ClearProgressPercent) item.ProgressPercent = null;
        else if (req.ProgressPercent.HasValue) item.ProgressPercent = req.ProgressPercent;
        if (req.AccentColor is not null) item.AccentColor = WorkItemMutationRules.Accent(req.AccentColor);
        if (req.StateId.HasValue)
        {
            await WorkItemMutationRules.ValidateTransitionAsync(_db, _permissions, item, req.StateId.Value);
            var state = await WorkItemMutationRules.StateAsync(_db, projectId, req.StateId.Value);
            item.StateId = state.Id; item.Category = state.Category;
        }
        Touch(item);
        Audit(item, "Patch", before);
        await SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(id);
    }

    public async Task<WorkItemDetailDto> TransitionAsync(Guid id, Guid toStateId, string? reason, string? rowVersion = null)
    {
        var projectId = await RequireMutationProjectAsync(id, Permission.CambiarEstado);
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.CambiarEstado);
        var item = await LoadMutationAsync(id, rowVersion);
        var before = Snapshot(item);
        reason = WorkItemMutationRules.Text(reason, 4000, "El motivo");
        await WorkItemMutationRules.ValidateTransitionAsync(_db, _permissions, item, toStateId, reason);
        var state = await WorkItemMutationRules.StateAsync(_db, projectId, toStateId);
        item.StateId = state.Id; item.Category = state.Category;
        Touch(item);
        Audit(item, "Transition", before, reason);
        await SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(id);
    }

    public async Task<ArchivePreviewDto> ArchivePreviewAsync(Guid id, bool restore = false)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var item = await _db.WorkItems.AsNoTracking().FirstAsync(w => w.Id == id && w.OrganizationId == OrgId);
        if (!item.ProjectId.HasValue) throw new ValidationException("La tarea debe pertenecer a un proyecto.");
        var items = await _db.WorkItems.AsNoTracking().Where(w => w.ProjectId == item.ProjectId && w.OrganizationId == OrgId).ToListAsync();
        var branch = Branch(item.Id, items).Where(w => w.IsArchived == restore).ToList();
        var permissions = await _permissions.GetProjectPermissionsAsync(item.ProjectId.Value);
        return new ArchivePreviewDto(branch.Count, branch.Select(w => new ArchiveTaskDto(w.Id, w.Code, w.Title)).ToList(), permissions.Contains(Permission.Archivar));
    }

    public async Task<WorkItemDetailDto> ArchiveAsync(Guid id, ArchiveWorkItemRequest req, bool restore = false)
    {
        var projectId = await RequireMutationProjectAsync(id, Permission.Archivar);
        await using var transaction = await WorkItemMutationRules.LockProjectAsync(_db, projectId, OrgId);
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Archivar);
        var item = await LoadMutationAsync(id, req.RowVersion, allowArchived: true);
        var items = await _db.WorkItems.Where(w => w.ProjectId == projectId && w.OrganizationId == OrgId).ToListAsync();
        foreach (var node in items.Where(w => w.Id != item.Id)) await _db.Entry(node).ReloadAsync();
        var branch = Branch(id, items);
        if (!restore && !req.IncludeDescendants && branch.Any(w => w.Id != id && !w.IsArchived))
            throw new ValidationException("La tarjeta tiene descendientes. Archiva la rama completa o reasigna primero las hijas.");
        if (restore && item.ParentTaskId.HasValue && items.FirstOrDefault(w => w.Id == item.ParentTaskId)?.IsArchived == true)
            throw new ValidationException("Restaura primero la madre o reasigna esta tarjeta a una madre activa.");
        var affected = (req.IncludeDescendants ? branch : branch.Where(w => w.Id == id)).Where(w => w.IsArchived == restore).ToList();
        foreach (var node in affected)
        {
            var before = Snapshot(node);
            node.IsArchived = !restore;
            Touch(node);
            Audit(node, restore ? "Restore" : "Archive", before);
        }
        await TouchAncestorsAsync(item.ParentTaskId);
        await SaveChangesAsync();
        await transaction.CommitAsync();
        return await GetAsync(id);
    }

    private static List<WorkItem> Branch(Guid rootId, IReadOnlyList<WorkItem> items)
    {
        var byId = items.ToDictionary(w => w.Id);
        if (!byId.TryGetValue(rootId, out var root)) throw new NotFoundException("Tarea no encontrada.");
        var children = items.Where(w => w.ParentTaskId.HasValue).GroupBy(w => w.ParentTaskId!.Value).ToDictionary(g => g.Key, g => g.ToList());
        var result = new List<WorkItem>();
        var queue = new Queue<WorkItem>(); queue.Enqueue(root);
        var visited = new HashSet<Guid>();
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            if (!visited.Add(node.Id)) throw new ValidationException("La jerarquía existente contiene un ciclo.");
            result.Add(node);
            if (children.TryGetValue(node.Id, out var descendants)) foreach (var child in descendants) queue.Enqueue(child);
        }
        return result;
    }

    private async Task<Guid> RequireMutationProjectAsync(Guid id, Permission permission)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var item = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id && w.OrganizationId == OrgId)
            ?? throw new NotFoundException("Tarea no encontrada.");
        if (!item.ProjectId.HasValue) throw new ValidationException("La tarea debe pertenecer a un proyecto.");
        await _permissions.EnsureProjectPermissionAsync(item.ProjectId.Value, permission);
        return item.ProjectId.Value;
    }

    private async Task<WorkItem> LoadMutationAsync(Guid id, string? rowVersion, bool allowArchived = false)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var item = await _db.WorkItems.Include(w => w.State).Include(w => w.Project)
            .FirstAsync(w => w.Id == id && w.OrganizationId == OrgId);
        await _db.Entry(item).ReloadAsync();
        WorkItemMutationRules.RequireVersion(item, rowVersion);
        if (!allowArchived && item.IsArchived) throw new ValidationException("La tarea está archivada. Restáurala antes de editarla.");
        return item;
    }

    private async Task ValidateRelatedFieldsAsync(Guid projectId, Guid? cycleId, Guid? epicId, Guid? releaseId)
    {
        if (cycleId.HasValue && !await _db.Cycles.AnyAsync(c => c.Id == cycleId && c.ProjectId == projectId))
            throw new ValidationException("El ciclo no pertenece al proyecto.");
        if (epicId.HasValue && !await _db.Epics.AnyAsync(e => e.Id == epicId && e.ProjectId == projectId))
            throw new ValidationException("La épica no pertenece al proyecto.");
        if (releaseId.HasValue && !await _db.Releases.AnyAsync(r => r.Id == releaseId && r.ProjectId == projectId))
            throw new ValidationException("La entrega no pertenece al proyecto.");
    }

    private void Touch(WorkItem item) { item.UpdatedAt = DateTime.UtcNow; item.UpdatedBy = UserId; }

    private async Task TouchAncestorsAsync(Guid? parentId)
    {
        var visited = new HashSet<Guid>();
        while (parentId.HasValue && visited.Add(parentId.Value))
        {
            var parent = await _db.WorkItems.FirstOrDefaultAsync(w => w.Id == parentId && w.OrganizationId == OrgId);
            if (parent is null) break;
            Touch(parent);
            parentId = parent.ParentTaskId;
        }
    }

    private static string Snapshot(WorkItem item) => System.Text.Json.JsonSerializer.Serialize(new
    {
        item.Title, item.Description, item.Notes, item.Priority, item.StateId, item.ParentTaskId,
        item.ProgressPercent, item.AccentColor, item.AssigneeId, item.IsArchived,
    });

    private void Audit(WorkItem item, string action, string? before, string? reason = null) => _db.AuditLogs.Add(new AuditLog
    {
        OrganizationId = OrgId, ActorId = UserId, Action = action, EntityType = "WorkItem", EntityId = item.Id,
        OldState = before, NewState = Snapshot(item), Reason = reason, CreatedBy = UserId,
    });

    private async Task NotifyAssignmentAsync(WorkItem item, Guid? previousAssignee)
    {
        if (item.AssigneeId.HasValue && item.AssigneeId != previousAssignee && item.AssigneeId != UserId)
            await NotificationHelper.NotifyAsync(_db, OrgId, item.AssigneeId.Value, NotificationEvent.Asignacion,
                $"Se te asignó la tarea {item.Code} — {item.Title}.", "WorkItem", item.Id, $"/tasks/{item.Id}",
                $"assign:{item.Id}:{item.AssigneeId}:{Guid.NewGuid():N}");
    }

    private async Task SaveChangesAsync()
    {
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }

    public async Task<CommentDto> AddCommentAsync(Guid id, string body)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var w = await _db.WorkItems.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new NotFoundException($"Tarea {id}");
        if (w.ProjectId.HasValue)
            await _permissions.EnsureProjectPermissionAsync(w.ProjectId.Value, Permission.Comentar);

        var c = new WorkItemComment { WorkItemId = id, AuthorId = UserId, Body = body, CreatedBy = UserId };
        _db.WorkItemComments.Add(c);
        await _db.SaveChangesAsync();

        if (w.AssigneeId.HasValue && w.AssigneeId.Value != UserId)
            await NotificationHelper.NotifyAsync(_db, OrgId, w.AssigneeId.Value, NotificationEvent.Respuesta,
                $"Nuevo comentario en {w.Code} — {w.Title}.", "WorkItem", w.Id, $"/tasks/{w.Id}",
                $"resp:{c.Id}:{w.AssigneeId.Value}");
        await NotificationHelper.NotifyMentionsAsync(_db, OrgId, UserId, body, "WorkItem", w.Id, $"/tasks/{w.Id}");
        await _db.SaveChangesAsync();

        var names = await UserNames.ResolveAsync(_db, new[] { (Guid?)c.AuthorId });
        return c.ToDto(names);
    }

    public async Task<List<CommentDto>> GetCommentsAsync(Guid id)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var comments = await _db.WorkItemComments.AsNoTracking().Where(c => c.WorkItemId == id && !c.IsDeleted)
            .OrderBy(c => c.CreatedAt).ToListAsync();
        var names = await UserNames.ResolveAsync(_db, comments.Select(c => (Guid?)c.AuthorId));
        return comments.Select(c => c.ToDto(names)).ToList();
    }

    public async Task<List<ChecklistDto>> GetChecklistsAsync(Guid id)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var lists = await _db.Checklists.AsNoTracking().Include(c => c.Items)
            .Where(c => c.WorkItemId == id).OrderBy(c => c.Order).ToListAsync();
        return lists.Select(c => c.ToDto()).ToList();
    }

    public async Task<ChecklistDto> AddChecklistAsync(Guid workItemId, string title)
    {
        await EnsureEditableResourceAsync(workItemId);
        title = WorkItemMutationRules.Title(title);
        var list = new Checklist { WorkItemId = workItemId, Title = title, Order = 0, CreatedBy = UserId };
        _db.Checklists.Add(list);
        await _db.SaveChangesAsync();
        return list.ToDto();
    }

    public async Task<ChecklistItemDto> AddChecklistItemAsync(Guid checklistId, string text, Guid? assigneeId)
    {
        var list = await _db.Checklists.FirstOrDefaultAsync(c => c.Id == checklistId)
            ?? throw new NotFoundException($"Checklist {checklistId}");
        var task = await EnsureEditableResourceAsync(list.WorkItemId);
        text = WorkItemMutationRules.Title(text);
        await WorkItemMutationRules.ValidateAssigneeAsync(_db, _permissions, task.ProjectId!.Value, OrgId, assigneeId, null);
        var maxOrder = await _db.ChecklistItems.Where(i => i.ChecklistId == checklistId).Select(i => (int?)i.Order).MaxAsync() ?? 0;
        var item = new ChecklistItem { ChecklistId = checklistId, Text = text, AssigneeId = assigneeId, Order = maxOrder + 1, CreatedBy = UserId };
        _db.ChecklistItems.Add(item);
        await _db.SaveChangesAsync();
        return item.ToDto();
    }

    public async Task ToggleChecklistItemAsync(Guid itemId)
    {
        var item = await _db.ChecklistItems.FirstOrDefaultAsync(i => i.Id == itemId)
            ?? throw new NotFoundException($"Ítem {itemId}");
        var list = await _db.Checklists.FirstAsync(c => c.Id == item.ChecklistId);
        await EnsureEditableResourceAsync(list.WorkItemId);
        item.IsCompleted = !item.IsCompleted;
        item.CompletedBy = item.IsCompleted ? UserId : null;
        item.CompletedAt = item.IsCompleted ? DateTime.UtcNow : null;
        await _db.SaveChangesAsync();
    }

    public async Task SetBlockedAsync(Guid id, bool blocked, string? reason)
    {
        await EnsureEditableResourceAsync(id);
        var w = await _db.WorkItems.FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new NotFoundException($"Tarea {id}");
        w.IsBlocked = blocked;
        w.BlockReason = blocked ? WorkItemMutationRules.Text(reason, 4000, "El motivo del bloqueo") : null;
        if (blocked) { w.BlockedAt = DateTime.UtcNow; w.BlockResolvedAt = null; w.BlockResolverId = null; }
        else { w.BlockResolvedAt = DateTime.UtcNow; w.BlockResolverId = UserId; }
        w.UpdatedBy = UserId;
        await _db.SaveChangesAsync();
    }

    public async Task AddRelationAsync(Guid id, Guid targetId, WorkRelationType type)
    {
        await EnsureEditableResourceAsync(id);
        await _permissions.EnsureCanReadWorkItemAsync(targetId);
        WorkItemMutationRules.EnumValue(type, "Relación");
        if (type is WorkRelationType.Padre or WorkRelationType.Subtarea)
            throw new ValidationException("Usa la operación de cambiar madre para modificar la jerarquía del mapa.");
        if (id == targetId) throw new ValidationException("Una tarea no puede relacionarse consigo misma.");
        if (type == WorkRelationType.Bloquea) await EnsureEditableResourceAsync(targetId);

        var blocking = type is WorkRelationType.Bloquea or WorkRelationType.BloqueadaPor;
        if (blocking && await WouldCreateCycleAsync(id, targetId))
            throw new InvalidStateException("La relación crearía una dependencia circular.");

        var exists = await _db.WorkRelations.AnyAsync(r =>
            r.SourceId == id && r.TargetId == targetId && r.Type == type);
        if (exists) return;

        _db.WorkRelations.Add(new WorkRelation
        {
            OrganizationId = OrgId,
            SourceId = id,
            TargetId = targetId,
            Type = type,
            CreatedBy = UserId,
        });

        if (type == WorkRelationType.Bloquea)
        {
            var target = await _db.WorkItems.FirstAsync(x => x.Id == targetId);
            target.IsBlocked = true;
            target.BlockedByWorkItemId = id;
            target.BlockedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync();
    }

    public async Task<List<WorkflowStateDto>> GetAvailableTransitionsAsync(Guid id)
    {
        await _permissions.EnsureCanReadWorkItemAsync(id);
        var w = await _db.WorkItems.AsNoTracking().FirstAsync(x => x.Id == id);
        var states = await _db.WorkflowStates.AsNoTracking()
            .Where(s => s.ProjectId == w.ProjectId && !s.IsArchived)
            .OrderBy(s => s.Order).ToListAsync();
        return states.Select(s => s.ToDto()).ToList();
    }

    private async Task<Guid> ResolveDefaultStateAsync(Guid projectId)
    {
        var state = await _db.WorkflowStates.AsNoTracking()
            .FirstOrDefaultAsync(s => s.ProjectId == projectId && s.IsDefault);
        return state?.Id ?? throw new InvalidStateException("El proyecto no tiene un estado por defecto.");
    }

    private async Task<WorkItem> EnsureEditableResourceAsync(Guid id)
    {
        await RequireMutationProjectAsync(id, Permission.Editar);
        var item = await _db.WorkItems.AsNoTracking().FirstAsync(w => w.Id == id && w.OrganizationId == OrgId);
        if (item.IsArchived) throw new ValidationException("La tarea está archivada. Restáurala antes de editarla.");
        return item;
    }

    private async Task<bool> WouldCreateCycleAsync(Guid source, Guid target)
    {
        // BFS por relaciones "Bloquea" desde target hasta source
        var visited = new HashSet<Guid> { target };
        var queue = new Queue<Guid>();
        queue.Enqueue(target);
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            if (cur == source) return true;
            var next = await _db.WorkRelations.AsNoTracking()
                .Where(r => r.SourceId == cur && r.Type == WorkRelationType.Bloquea)
                .Select(r => r.TargetId).ToListAsync();
            foreach (var n in next)
                if (visited.Add(n)) queue.Enqueue(n);
        }
        return false;
    }
}
