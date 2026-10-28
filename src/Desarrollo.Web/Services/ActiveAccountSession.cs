using System.Security.Claims;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Desarrollo.Web.Services;

public static class ActiveAccountSession
{
    public static async Task<bool> IsActiveAsync(AppDbContext db, Guid userId, Guid organizationId) =>
        await db.Users.AsNoTracking().AnyAsync(u => u.Id == userId && u.IsActive)
        && await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.IsActive);

    public static async Task ValidateAsync(CookieValidatePrincipalContext context)
    {
        if (context.Principal?.Identity?.IsAuthenticated != true) return;
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        if (!Guid.TryParse(context.Principal.FindFirstValue(ClaimTypes.NameIdentifier), out var userId)
            || !Guid.TryParse(context.Principal.FindFirstValue("org_id"), out var orgId)
            || !await IsActiveAsync(db, userId, orgId))
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
        }
    }
}

/// <summary>Keep the organization claim when Identity refreshes a cookie/security stamp.</summary>
public sealed class CrmClaimsPrincipalFactory(UserManager<AppUser> users, RoleManager<IdentityRole<Guid>> roles,
    IOptions<IdentityOptions> options, AppDbContext db) : UserClaimsPrincipalFactory<AppUser, IdentityRole<Guid>>(users, roles, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(AppUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var orgId = await db.OrganizationMembers.AsNoTracking().Where(m => m.UserId == user.Id && m.IsActive)
            .OrderBy(m => m.JoinedAt).ThenBy(m => m.Id).Select(m => (Guid?)m.OrganizationId).FirstOrDefaultAsync();
        if (orgId.HasValue) identity.AddClaim(new Claim("org_id", orgId.Value.ToString()));
        identity.AddClaim(new Claim("name", user.FullName));
        return identity;
    }
}
