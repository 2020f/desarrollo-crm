using System.Data;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Integrations;
using Desarrollo.Domain.Tickets;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Desarrollo.Infrastructure.Services;

public class TicketService : ITicketService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public TicketService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    { _db = db; _user = user; _permissions = permissions; }

    public async Task<Paged<TicketDto>> ListAsync(TicketQuery query)
    {
        var scope = await ScopeAsync();
        if (query.Page < 1 || query.Page > 1000000 || query.PageSize < 1 || query.PageSize > 100)
            throw new ValidationException("La paginación no es válida.");
        if (query.Status.HasValue) WorkItemMutationRules.EnumValue(query.Status.Value, "Estado");
        if (query.Search?.Length > 250) throw new ValidationException("La búsqueda admite hasta 250 caracteres.");
        if (query.ProjectId.HasValue && !scope.ProjectPermissions.ContainsKey(query.ProjectId.Value)) throw new ForbiddenException();
        var q = TicketAccessRules.ReadableQuery(_db, scope).Include(t => t.Project).AsQueryable();
        if (query.ProjectId.HasValue) q = q.Where(t => t.ProjectId == query.ProjectId.Value);
        if (query.Status.HasValue) q = q.Where(t => t.Status == query.Status.Value);
        var search = query.Search?.Trim();
        if (!string.IsNullOrEmpty(search)) q = q.Where(t => t.Title.Contains(search) || t.Code.Contains(search));
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(t => t.CreatedAt).ThenBy(t => t.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();
        var names = await UserNames.ResolveAsync(_db, rows.Select(t => t.AssigneeId));
        return new(rows.Select(t => Dto(t, names, scope)).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<TicketDto> CreateAsync(CreateTicketRequest req)
    {
        var scope = await ScopeAsync();
        var prototype = new Ticket { OrganizationId = scope.OrganizationId, RequesterId = scope.UserId, ProjectId = req.ProjectId };
        TicketAccessRules.Require(prototype, scope, Permission.Crear);
        var title = WorkItemMutationRules.Title(req.Title);
        WorkItemMutationRules.EnumValue(req.Category, "Categoría");
        WorkItemMutationRules.EnumValue(req.Severity, "Severidad");
        WorkItemMutationRules.EnumValue(req.Priority, "Prioridad");
        var description = WorkItemMutationRules.Text(req.Description, 20000, "La descripción");
        var reproSteps = WorkItemMutationRules.Text(req.ReproSteps, 20000, "Los pasos de reproducción");
        var version = WorkItemMutationRules.Text(req.AffectedVersion, 200, "La versión");
        var environment = WorkItemMutationRules.Text(req.Environment, 1000, "El ambiente");
        await using var transaction = await LockOrganizationAsync(scope.OrganizationId);
        scope = await ScopeAsync();
        TicketAccessRules.Require(prototype, scope, Permission.Crear);
        if (req.AssigneeId.HasValue) TicketAccessRules.Require(prototype, scope, Permission.Asignar);
        await TicketAccessRules.ValidateAssigneeAsync(_db, scope, req.ProjectId, req.AssigneeId);
        var project = req.ProjectId.HasValue ? await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == req.ProjectId) : null;
        var productId = req.ProductId ?? project?.ProductId;
        if (productId.HasValue && (!await _db.Products.AsNoTracking().AnyAsync(p => p.Id == productId && p.OrganizationId == scope.OrganizationId && p.IsActive)
            || (project?.ProductId.HasValue == true && project.ProductId != productId)))
            throw new ValidationException("El producto debe estar activo y ser compatible con la organización y el proyecto.");
        var max = await _db.Tickets.Where(t => t.OrganizationId == scope.OrganizationId).Select(t => (int?)t.Number).MaxAsync() ?? 0;
        if (max == int.MaxValue) throw new ValidationException("Se agotó la numeración de solicitudes.");
        var ticket = new Ticket
        {
            OrganizationId = scope.OrganizationId, Number = max + 1, Code = $"TKT-{max + 1}", RequesterId = scope.UserId,
            Title = title, Category = req.Category, Description = description, ReproSteps = reproSteps,
            ProductId = productId, ProjectId = req.ProjectId, Severity = req.Severity, Priority = req.Priority,
            AffectedVersion = version, Environment = environment, AssigneeId = req.AssigneeId,
            Status = TicketStatus.Recibido, CreatedBy = scope.UserId, UpdatedBy = scope.UserId,
        };
        _db.Tickets.Add(ticket);
        await NotifyAssignmentAsync(ticket, null, scope);
        Audit(ticket, scope, "TicketCreate", null, TicketStatus.Recibido.ToString());
        await SaveAsync();
        await transaction.CommitAsync();
        return await GetAsync(ticket.Id);
    }

    public async Task<TicketDto> GetAsync(Guid id)
    {
        var scope = await ScopeAsync();
        var ticket = await TicketAccessRules.ReadableQuery(_db, scope).Include(t => t.Project)
            .FirstOrDefaultAsync(t => t.Id == id) ?? throw new NotFoundException("Solicitud no encontrada.");
        return Dto(ticket, await UserNames.ResolveAsync(_db, new[] { ticket.AssigneeId }), scope);
    }

    public async Task<TicketDto> AssignAsync(Guid id, Guid? assigneeId)
    {
        var scope = await ScopeAsync();
        var ticket = await LoadMutationAsync(id, scope, Permission.Asignar);
        await TicketAccessRules.ValidateAssigneeAsync(_db, scope, ticket.ProjectId, assigneeId);
        var previous = ticket.AssigneeId;
        if (previous == assigneeId) return await GetAsync(id);
        ticket.AssigneeId = assigneeId;
        Touch(ticket, scope);
        Audit(ticket, scope, "TicketAssign", previous?.ToString(), assigneeId?.ToString());
        await NotifyAssignmentAsync(ticket, previous, scope);
        await SaveAsync();
        return Dto(ticket, await UserNames.ResolveAsync(_db, new[] { ticket.AssigneeId }), scope);
    }

    public async Task<TicketDto> SetStatusAsync(Guid id, TicketStatus status, string? reason)
    {
        var scope = await ScopeAsync();
        WorkItemMutationRules.EnumValue(status, "Estado");
        reason = WorkItemMutationRules.Text(reason, 4000, "El motivo");
        var ticket = await LoadMutationAsync(id, scope, Permission.CambiarEstado);
        var previous = ticket.Status;
        if (previous == status) return await GetAsync(id);
        ticket.Status = status;
        if (status == TicketStatus.Aceptado) ticket.AcceptedAt = DateTime.UtcNow;
        if (status == TicketStatus.Resuelto) ticket.ResolvedAt = DateTime.UtcNow;
        if (status == TicketStatus.Cerrado) ticket.ClosedAt = DateTime.UtcNow;
        if (status == TicketStatus.Rechazado) ticket.RejectReason = reason;
        if (status == TicketStatus.Reabierto) ticket.ReopenReason = reason;
        Touch(ticket, scope);
        Audit(ticket, scope, "TicketStatus", previous.ToString(), status.ToString(), reason);
        await SaveAsync();
        return await GetAsync(id);
    }

    public async Task<TicketCommentDto> AddCommentAsync(Guid id, string body, bool isInternal)
    {
        var scope = await ScopeAsync();
        var ticket = await LoadMutationAsync(id, scope, Permission.Comentar);
        if (isInternal) TicketAccessRules.Require(ticket, scope, Permission.Editar);
        body = WorkItemMutationRules.Text(body, 20000, "El comentario") ?? throw new ValidationException("El comentario no puede estar vacío.");
        var comment = new TicketComment { TicketId = id, AuthorId = scope.UserId, Body = body, IsInternal = isInternal,
            CreatedBy = scope.UserId, UpdatedBy = scope.UserId };
        _db.TicketComments.Add(comment);
        await SaveAsync();
        return comment.ToDto(await UserNames.ResolveAsync(_db, new[] { (Guid?)scope.UserId }));
    }

    public async Task<List<TicketCommentDto>> GetCommentsAsync(Guid id)
    {
        var scope = await ScopeAsync();
        var ticket = await TicketAccessRules.ReadableQuery(_db, scope).FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException("Solicitud no encontrada.");
        var internalRead = TicketAccessRules.PermissionsFor(ticket, scope).Contains(Permission.Editar);
        var comments = await _db.TicketComments.AsNoTracking().Where(c => c.TicketId == id && !c.IsDeleted && (!c.IsInternal || internalRead))
            .OrderBy(c => c.CreatedAt).ThenBy(c => c.Id).ToListAsync();
        var names = await UserNames.ResolveAsync(_db, comments.Select(c => (Guid?)c.AuthorId));
        return comments.Select(c => c.ToDto(names)).ToList();
    }

    public async Task LinkWorkItemAsync(Guid id, Guid workItemId)
    {
        var scope = await ScopeAsync();
        await using var transaction = await LockOrganizationAsync(scope.OrganizationId);
        scope = await ScopeAsync();
        var ticket = await LoadMutationAsync(id, scope, Permission.Editar);
        var work = await _db.WorkItems.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workItemId
            && w.OrganizationId == scope.OrganizationId && !w.IsArchived && w.Category != WorkItemCategory.Cancelado)
            ?? throw new NotFoundException("Tarea no encontrada.");
        if (work.ProjectId != ticket.ProjectId) throw new ValidationException("La tarea y la solicitud deben pertenecer al mismo proyecto.");
        await _permissions.EnsureCanReadWorkItemAsync(workItemId);
        if (work.ProjectId.HasValue)
        {
            await _permissions.EnsureProjectPermissionAsync(work.ProjectId.Value, Permission.Leer);
            await _permissions.EnsureProjectPermissionAsync(work.ProjectId.Value, Permission.Editar);
        }
        else if (work.AuthorId != scope.UserId) throw new ForbiddenException();
        if (!await _db.TicketWorkItemLinks.AnyAsync(l => l.TicketId == id && l.WorkItemId == workItemId))
        {
            _db.TicketWorkItemLinks.Add(new TicketWorkItemLink { TicketId = id, WorkItemId = workItemId, CreatedBy = scope.UserId });
            Audit(ticket, scope, "TicketLinkWorkItem", null, workItemId.ToString());
            await SaveAsync();
        }
        await transaction.CommitAsync();
    }

    private Task<TicketAccessScope> ScopeAsync() => TicketAccessRules.LoadScopeAsync(_db, _user, _permissions);

    private async Task<Ticket> LoadMutationAsync(Guid id, TicketAccessScope scope, Permission permission)
    {
        if (!await TicketAccessRules.ReadableQuery(_db, scope).AnyAsync(t => t.Id == id))
            throw new NotFoundException("Solicitud no encontrada.");
        var ticket = await _db.Tickets.Include(t => t.Project).FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == scope.OrganizationId)
            ?? throw new NotFoundException("Solicitud no encontrada.");
        await _db.Entry(ticket).ReloadAsync();
        TicketAccessRules.Require(ticket, scope, permission);
        return ticket;
    }

    private static TicketDto Dto(Ticket ticket, Dictionary<Guid, string> names, TicketAccessScope scope)
    {
        var permissions = TicketAccessRules.PermissionsFor(ticket, scope);
        var readable = permissions.Contains(Permission.Leer) && (ticket.ProjectId.HasValue
            || ticket.RequesterId == scope.UserId || ticket.AssigneeId == scope.UserId);
        return ticket.ToDto(names) with
        {
            CanEdit = readable && permissions.Contains(Permission.Editar), CanAssign = readable && permissions.Contains(Permission.Asignar),
            CanChangeStatus = readable && permissions.Contains(Permission.CambiarEstado), CanComment = readable && permissions.Contains(Permission.Comentar),
            CanReadInternalNotes = readable && permissions.Contains(Permission.Editar),
            CanWriteInternalNotes = readable && permissions.Contains(Permission.Editar) && permissions.Contains(Permission.Comentar),
            CanLinkWorkItems = readable && permissions.Contains(Permission.Editar),
        };
    }

    private async Task NotifyAssignmentAsync(Ticket ticket, Guid? previous, TicketAccessScope scope)
    {
        if (ticket.AssigneeId.HasValue && ticket.AssigneeId != previous && ticket.AssigneeId != scope.UserId)
            await NotificationHelper.NotifyAsync(_db, scope.OrganizationId, ticket.AssigneeId.Value, NotificationEvent.Asignacion,
                $"Se te asignó la solicitud {ticket.Code} — {ticket.Title}.", "Ticket", ticket.Id, $"/tickets/{ticket.Id}",
                $"ticket-assign:{ticket.Id}:{ticket.AssigneeId}:{Guid.NewGuid():N}");
    }

    private async Task<IDbContextTransaction> LockOrganizationAsync(Guid organizationId)
    {
        var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            await _db.Database.ExecuteSqlInterpolatedAsync($"SELECT [Id] FROM [Organizations] WITH (UPDLOCK,HOLDLOCK) WHERE [Id]={organizationId}");
            return transaction;
        }
        catch { await transaction.DisposeAsync(); throw; }
    }

    private static void Touch(Ticket ticket, TicketAccessScope scope) { ticket.UpdatedAt = DateTime.UtcNow; ticket.UpdatedBy = scope.UserId; }
    private void Audit(Ticket ticket, TicketAccessScope scope, string action, string? before, string? after, string? reason = null) => _db.AuditLogs.Add(new AuditLog
    { OrganizationId = scope.OrganizationId, ActorId = scope.UserId, Action = action, EntityType = "Ticket", EntityId = ticket.Id,
        OldState = before, NewState = after, Reason = reason, CreatedBy = scope.UserId });
    private async Task SaveAsync()
    {
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }
}
