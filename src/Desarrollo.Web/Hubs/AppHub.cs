using System.Security.Claims;
using Desarrollo.Infrastructure.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Web.Hubs;

[Authorize]
public class AppHub(AppDbContext db) : Hub
{
    public override async Task OnConnectedAsync()
    {
        if (Context.User?.Identity?.IsAuthenticated != true
            || !Guid.TryParse(Context.User.FindFirstValue("org_id"), out var orgId) || orgId == Guid.Empty
            || !Guid.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId) || userId == Guid.Empty
            || !await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive, Context.ConnectionAborted)
            || !await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == orgId
                && m.UserId == userId && m.IsActive, Context.ConnectionAborted))
        {
            Context.Abort();
            return;
        }
        await Groups.AddToGroupAsync(Context.ConnectionId, $"org:{orgId:D}", Context.ConnectionAborted);
        await Groups.AddToGroupAsync(Context.ConnectionId, $"user:{userId:D}", Context.ConnectionAborted);
        await base.OnConnectedAsync();
    }
}
