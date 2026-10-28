using System.Data;
using System.Text.Json;
using System.Text.RegularExpressions;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain;
using Desarrollo.Domain.Common;
using Desarrollo.Domain.Work;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Desarrollo.Infrastructure.Services;

internal static partial class WorkItemMutationRules
{
    // Every structural entry point acquires the same database lock. Rowversion alone
    // cannot prevent edits of distinct tasks from introducing a hierarchy cycle.
    public static async Task<IDbContextTransaction> LockProjectAsync(AppDbContext db, Guid projectId, Guid organizationId)
    {
        var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Projects] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {projectId} AND [OrganizationId] = {organizationId} AND [IsDeleted] = 0");
            if (!await db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId && p.OrganizationId == organizationId))
                throw new ForbiddenException();
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    public static string Title(string? title)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Trim().Length > 250)
            throw new ValidationException("El nombre debe contener entre 1 y 250 caracteres.");
        return title.Trim();
    }

    public static string? Text(string? text, int limit, string name)
    {
        if (text?.Length > limit) throw new ValidationException($"{name} admite hasta {limit} caracteres.");
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static string? Accent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (!AccentPattern().IsMatch(value)) throw new ValidationException("El acento debe ser un color hexadecimal #RRGGBB.");
        return value.ToUpperInvariant();
    }
    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex AccentPattern();

    public static void Progress(int? progress)
    {
        if (progress is < 0 or > 100) throw new ValidationException("El progreso debe estar entre 0 y 100.");
    }
    public static void EnumValue<T>(T value, string name) where T : struct, Enum
    {
        if (!Enum.IsDefined(value)) throw new ValidationException($"{name} no válido.");
    }

    public static void RequireVersion(Entity entity, string? rowVersion)
    {
        if (string.IsNullOrWhiteSpace(rowVersion)) throw new ValidationException("Debes enviar la versión del registro que estás editando.");
        byte[] supplied;
        try { supplied = Convert.FromBase64String(rowVersion); }
        catch (FormatException) { throw new ValidationException("Token de versión no válido."); }
        if (supplied.Length != 8) throw new ValidationException("Token de versión no válido.");
        if (!entity.RowVersion.SequenceEqual(supplied))
            throw new ConcurrencyConflictException(current: new { entity.Id, RowVersion = Convert.ToBase64String(entity.RowVersion) });
    }

    public static async Task ValidateParentAsync(AppDbContext db, IPermissionService permissions,
        Guid projectId, Guid organizationId, Guid? taskId, Guid? parentId, bool allowArchived = false)
    {
        if (!parentId.HasValue) return;
        if (parentId == taskId) throw new ValidationException("Una tarea no puede ser su propia madre.");
        await permissions.EnsureCanReadWorkItemAsync(parentId.Value);
        var items = await db.WorkItems.AsNoTracking()
            .Where(w => w.ProjectId == projectId && w.OrganizationId == organizationId)
            .Select(w => new { w.Id, w.ParentTaskId, w.IsArchived }).ToDictionaryAsync(w => w.Id);
        if (!items.TryGetValue(parentId.Value, out var parent))
            throw new ValidationException("La madre y la hija deben pertenecer al mismo proyecto.");
        if (parent.IsArchived && !allowArchived) throw new ValidationException("La tarea madre está archivada.");
        var visited = new HashSet<Guid>();
        Guid? current = parentId;
        while (current.HasValue)
        {
            if (current == taskId || !visited.Add(current.Value)) throw new ValidationException("La relación crearía una jerarquía circular.");
            if (!items.TryGetValue(current.Value, out var ancestor)) throw new ValidationException("La jerarquía contiene una madre fuera del proyecto o eliminada.");
            current = ancestor.ParentTaskId;
        }
    }

    public static async Task<WorkflowState> StateAsync(AppDbContext db, Guid projectId, Guid stateId) =>
        await db.WorkflowStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stateId && s.ProjectId == projectId && !s.IsArchived)
            ?? throw new ValidationException("El estado no está disponible en este proyecto.");

    public static async Task ValidateTransitionAsync(AppDbContext db, IPermissionService permissions, WorkItem item,
        Guid toStateId, string? reason = null)
    {
        if (!item.ProjectId.HasValue) throw new ValidationException("La tarea debe pertenecer a un proyecto.");
        var projectId = item.ProjectId.Value;
        var state = await StateAsync(db, projectId, toStateId);
        if (item.StateId == toStateId) return;
        await permissions.EnsureProjectPermissionAsync(projectId, Permission.CambiarEstado);
        var transitions = await db.Transitions.AsNoTracking().Where(t => t.ProjectId == projectId).ToListAsync();
        var transition = transitions.FirstOrDefault(t => t.FromStateId == item.StateId && t.ToStateId == toStateId);
        if (transitions.Count != 0 && transition is null) throw new ValidationException("La transición no está permitida por el flujo del proyecto.");
        if (state.Category == WorkItemCategory.Finalizado) await permissions.EnsureProjectPermissionAsync(projectId, Permission.Aprobar);
        if (transition is null) return;
        foreach (var permission in transition.RequiredPermissions) await permissions.EnsureProjectPermissionAsync(projectId, permission);
        if (string.IsNullOrWhiteSpace(transition.Conditions)) return;
        Dictionary<string, bool>? conditions;
        try { conditions = JsonSerializer.Deserialize<Dictionary<string, bool>>(transition.Conditions); }
        catch (JsonException) { throw new ValidationException("Las condiciones de la transición requieren una configuración válida."); }
        if (conditions is null) return;
        foreach (var condition in conditions.Where(c => c.Value))
        {
            var met = condition.Key switch
            {
                "requireAcceptanceCriteria" => !string.IsNullOrWhiteSpace(item.AcceptanceCriteria),
                "requireAssignee" => item.AssigneeId.HasValue,
                "requireDescription" => !string.IsNullOrWhiteSpace(item.Description),
                "requireReason" => !string.IsNullOrWhiteSpace(reason),
                "requireCompletedChecklist" => await db.ChecklistItems.AnyAsync(i => i.Checklist!.WorkItemId == item.Id)
                    && !await db.ChecklistItems.AnyAsync(i => i.Checklist!.WorkItemId == item.Id && !i.IsCompleted),
                _ => throw new ValidationException($"Condición de transición no soportada: {condition.Key}."),
            };
            if (!met) throw new ValidationException($"Falta cumplir la condición de transición: {condition.Key}.");
        }
    }

    public static async Task ValidateAssigneeAsync(AppDbContext db, IPermissionService permissions,
        Guid projectId, Guid organizationId, Guid? assigneeId, Guid? previousAssigneeId)
    {
        if (assigneeId == previousAssigneeId) return;
        await permissions.EnsureProjectPermissionAsync(projectId, Permission.Asignar);
        if (!assigneeId.HasValue) return;
        var member = await db.OrganizationMembers.AsNoTracking().FirstOrDefaultAsync(m =>
            m.OrganizationId == organizationId && m.UserId == assigneeId.Value && m.IsActive);
        if (member is null || !await db.Users.AnyAsync(u => u.Id == assigneeId && u.IsActive))
            throw new ValidationException("La persona responsable no es un miembro activo de la organización.");
        var project = await db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId);
        var projectMember = await db.ProjectMembers.AsNoTracking().FirstOrDefaultAsync(m => m.ProjectId == projectId && m.UserId == assigneeId && m.IsActive);
        var projectRole = projectMember?.RoleId is Guid roleId ? await db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == roleId && r.OrganizationId == organizationId) : null;
        var hasProject = projectMember is not null && (!projectMember.RoleId.HasValue || projectRole?.Permissions.Contains(Permission.Leer) == true);
        var role = member.RoleId.HasValue ? await db.OrganizationRoles.AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == member.RoleId && r.OrganizationId == organizationId) : null;
        var isAdmin = role is not null && Enum.GetValues<Permission>().All(role.Permissions.Contains);
        var canRead = isAdmin || (projectMember is not null ? hasProject : !project.IsPrivate);
        if (!canRead) throw new ValidationException("La persona responsable no tiene permiso de lectura en el proyecto.");
    }

    public static (string FullPath, int Depth) PathOf(WorkItem item, IReadOnlyDictionary<Guid, WorkItem> items)
    {
        var path = new List<string> { item.Title };
        var visited = new HashSet<Guid> { item.Id };
        var parentId = item.ParentTaskId;
        while (parentId.HasValue && items.TryGetValue(parentId.Value, out var parent) && visited.Add(parent.Id))
        {
            path.Add(parent.Title);
            parentId = parent.ParentTaskId;
        }
        path.Reverse();
        return (string.Join(" / ", path), path.Count - 1);
    }
}
