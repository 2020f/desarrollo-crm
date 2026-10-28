using System.Security.Claims;
using Desarrollo.Application.Abstractions;

namespace Desarrollo.Web.Services;

public class CurrentUserService : ICurrentUser
{
    private readonly IHttpContextAccessor _http;

    public CurrentUserService(IHttpContextAccessor http) => _http = http;

    private ClaimsPrincipal? User => _http.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var v = User?.FindFirstValue(ClaimTypes.NameIdentifier);
            return Guid.TryParse(v, out var id) ? id : null;
        }
    }

    public Guid? OrganizationId
    {
        get
        {
            var v = User?.FindFirstValue("org_id");
            return Guid.TryParse(v, out var id) ? id : null;
        }
    }

    public string Name => User?.FindFirstValue("name") ?? User?.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated == true;
}
