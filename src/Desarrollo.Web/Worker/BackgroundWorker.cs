using Desarrollo.Domain;
using Desarrollo.Domain.Notifications;
using Desarrollo.Infrastructure.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Web.Worker;

public class BackgroundWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<BackgroundWorker> _logger;

    public BackgroundWorker(IServiceScopeFactory scopeFactory, ILogger<BackgroundWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await ProcessOverdueNotificationsAsync(db, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error en BackgroundWorker");
            }
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    /// <summary>Genera un aviso de vencimiento por tarea vencida (idempotente por dedup key).</summary>
    private async Task ProcessOverdueNotificationsAsync(AppDbContext db, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var activeCategories = new[] { WorkItemCategory.Pendiente, WorkItemCategory.EnCurso };
        var overdue = await db.WorkItems.AsNoTracking()
            .Where(w => w.DueDate.HasValue && w.DueDate.Value < now
                && activeCategories.Contains(w.Category)
                && w.AssigneeId.HasValue)
            .Select(w => new { w.Id, w.Code, w.Title, w.AssigneeId, w.OrganizationId })
            .ToListAsync(ct);

        foreach (var w in overdue)
        {
            var dedup = $"due:{w.Id}:{w.AssigneeId}";
            if (await db.Notifications.AnyAsync(n => n.DedupKey == dedup, ct)) continue;

            db.Notifications.Add(new Notification
            {
                OrganizationId = w.OrganizationId,
                UserId = w.AssigneeId!.Value,
                Event = NotificationEvent.Vencimiento,
                Message = $"La tarea {w.Code} — {w.Title} está vencida.",
                EntityType = "WorkItem",
                EntityId = w.Id,
                Route = $"/tasks/{w.Id}",
                DedupKey = dedup,
            });
        }
        await db.SaveChangesAsync(ct);
    }
}
