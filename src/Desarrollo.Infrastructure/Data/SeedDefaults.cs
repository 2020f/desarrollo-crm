using Desarrollo.Domain;
using Desarrollo.Domain.Chat;
using Desarrollo.Domain.Work;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Data;

public static class SeedDefaults
{
    public static async Task EnsureDefaultStatesAsync(this AppDbContext db, Guid projectId, Guid orgId)
    {
        if (await db.WorkflowStates.AnyAsync(s => s.ProjectId == projectId)) return;

        var states = new (string Name, WorkItemCategory Cat, string Color)[]
        {
            ("Backlog", WorkItemCategory.Pendiente, "#A8B7D1"),
            ("Listo para desarrollar", WorkItemCategory.Pendiente, "#EAC385"),
            ("En desarrollo", WorkItemCategory.EnCurso, "#9B8AFB"),
            ("En revisión", WorkItemCategory.EnCurso, "#EAC385"),
            ("En pruebas", WorkItemCategory.EnCurso, "#57D6C7"),
            ("Listo para publicar", WorkItemCategory.EnCurso, "#57D6C7"),
            ("Publicada", WorkItemCategory.Finalizado, "#57D6C7"),
            ("Aceptada", WorkItemCategory.Finalizado, "#57D6C7"),
        };

        for (int i = 0; i < states.Length; i++)
        {
            db.WorkflowStates.Add(new WorkflowState
            {
                OrganizationId = orgId,
                ProjectId = projectId,
                Name = states[i].Name,
                Category = states[i].Cat,
                Color = states[i].Color,
                Order = i,
                IsDefault = i == 0,
            });
        }
    }

    public static async Task EnsureDefaultChannelsAsync(this AppDbContext db, Guid projectId, Guid orgId)
    {
        if (await db.Channels.AnyAsync(c => c.ProjectId == projectId)) return;
        db.Channels.Add(new Channel
        {
            OrganizationId = orgId,
            ProjectId = projectId,
            Type = ChannelType.Proyecto,
            Name = "general",
            Topic = "Canal general del proyecto",
        });
    }
}
