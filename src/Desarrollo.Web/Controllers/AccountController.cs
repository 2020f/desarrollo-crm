using System.Security.Claims;
using Desarrollo.Domain.Organizations;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Web.Seed;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Desarrollo.Web.Services;
using Desarrollo.Application.Common;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/account")]
public class AccountController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly SignInManager<AppUser> _signIn;
    private readonly AppDbContext _db;
    private readonly IAntiforgery _antiforgery;
    private readonly AccountProvisioningService _provisioning;
    private readonly RegistrationPolicy _registration;

    public AccountController(UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDbContext db, IAntiforgery antiforgery,
        AccountProvisioningService provisioning, RegistrationPolicy registration)
    {
        _users = users;
        _signIn = signIn;
        _db = db;
        _antiforgery = antiforgery;
        _provisioning = provisioning;
        _registration = registration;
    }

    public record LoginRequest(string Username, string Password, bool RememberMe);
    public record RegisterRequest(string FullName, string Email, string Password, string OrganizationName);
    public record CurrentUserResponse(Guid? Id, string Name, string? Email, Guid? OrganizationId, string? OrganizationName, bool IsAuthenticated);

    [AllowAnonymous]
    [HttpGet("registration")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public IActionResult Registration() => Ok(new { enabled = _registration.Enabled });

    [AllowAnonymous]
    [HttpGet("antiforgery")]
    public IActionResult Antiforgery()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new { token = tokens.RequestToken });
    }

    [AllowAnonymous]
    [HttpGet("current")]
    public async Task<ActionResult<CurrentUserResponse>> Current()
    {
        if (!User.Identity!.IsAuthenticated)
            return Unauthorized(new { code = "AUTH", message = "No autenticado." });

        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = userId is null ? null : await _users.FindByIdAsync(userId);
        Guid? orgId = Guid.TryParse(User.FindFirstValue("org_id"), out var o) ? o : null;
        string? orgName = null;
        if (orgId.HasValue)
            orgName = await _db.Organizations.AsNoTracking().Where(x => x.Id == orgId.Value).Select(x => x.Name).FirstOrDefaultAsync();

        return Ok(new CurrentUserResponse(
            user?.Id, user?.FullName ?? string.Empty, user?.Email, orgId, orgName, true));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("account-login")]
    public async Task<ActionResult<CurrentUserResponse>> Login([FromBody] LoginRequest req)
    {
        var user = await _users.FindByNameAsync(req.Username) ?? await _users.FindByEmailAsync(req.Username);
        if (user is null || !user.IsActive)
            return Unauthorized(new { code = "AUTH", message = "Credenciales inválidas." });

        var result = await _signIn.CheckPasswordSignInAsync(user, req.Password, lockoutOnFailure: true);
        if (result.IsLockedOut) return Unauthorized(new { code = "AUTH", message = "Cuenta bloqueada temporalmente." });
        if (!result.Succeeded) return Unauthorized(new { code = "AUTH", message = "Credenciales inválidas." });

        var principal = await _signIn.CreateUserPrincipalAsync(user);
        if (!Guid.TryParse(principal.FindFirstValue("org_id"), out var activeOrganization))
            return Unauthorized(new { code = "AUTH", message = "Credenciales inválidas o cuenta sin acceso activo." });
        await _signIn.SignInAsync(user, req.RememberMe);

        Guid? orgId = activeOrganization;
        var orgName = orgId.HasValue
            ? await _db.Organizations.AsNoTracking().Where(x => x.Id == orgId.Value).Select(x => x.Name).FirstOrDefaultAsync()
            : null;
        return Ok(new CurrentUserResponse(user.Id, user.FullName, user.Email, orgId, orgName, true));
    }

    [AllowAnonymous]
    [HttpPost("register")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("account-register")]
    public async Task<ActionResult<CurrentUserResponse>> Register([FromBody] RegisterRequest req)
    {
        if (!_registration.Enabled) throw new ForbiddenException("El registro público está cerrado. Solicita una cuenta al administrador.");
        var account = await _provisioning.CreateTeamAsync(new(req.FullName, req.Email, req.Password, req.OrganizationName));
        var user = account.User;
        var org = account.Organization;
        await _signIn.SignInAsync(user, isPersistent: true);
        return Ok(new CurrentUserResponse(user.Id, user.FullName, user.Email, org.Id, org.Name, true));
    }

    [HttpPost("logout")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return Ok(new { ok = true });
    }

}
