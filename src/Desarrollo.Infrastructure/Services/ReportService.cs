using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class ReportService : IReportService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public ReportService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    {
        _db = db;
        _user = user;
        _permissions = permissions;
    }

    public async Task<ReportSnapshotDto> GetProjectSnapshotAsync(Guid projectId)
    {
        await _permissions.EnsureProjectPermissionAsync(projectId, Permission.Leer);

        var items = await _db.WorkItems.AsNoTracking().Where(w => w.ProjectId == projectId).ToListAsync();
        var states = await _db.WorkflowStates.AsNoTracking().Where(s => s.ProjectId == projectId).ToListAsync();

        var now = DateTime.UtcNow;
        var total = items.Count;
        var done = items.Count(w => w.Category == WorkItemCategory.Finalizado);
        var overdue = items.Count(w => w.DueDate.HasValue && w.DueDate.Value < now && w.Category is WorkItemCategory.Pendiente or WorkItemCategory.EnCurso);
        var blocked = items.Count(w => w.IsBlocked);
        var wip = items.Count(w => w.Category == WorkItemCategory.EnCurso);

        var byState = states.Select(s => new ChartSlice(s.Id.ToString(), s.Name, items.Count(w => w.StateId == s.Id))).ToList();

        var openTicketStatuses = new[]
        {
            TicketStatus.Recibido, TicketStatus.EnEvaluacion, TicketStatus.Aceptado,
            TicketStatus.EnEjecucion, TicketStatus.Reabierto,
        };
        var openTickets = await _db.Tickets.CountAsync(t => t.ProjectId == projectId && openTicketStatuses.Contains(t.Status));

        return new ReportSnapshotDto(total, done, overdue, blocked, openTickets, 0, wip, byState);
    }
}
