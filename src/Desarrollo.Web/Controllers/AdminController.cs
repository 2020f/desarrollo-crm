using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain.Organizations;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Web.Controllers;

[ApiController]
[Route("api/admin")]
[Authorize]
public class AdminController : ControllerBase
{
    private readonly UserManager<AppUser> _users;
    private readonly AppDbContext _db;
    private readonly ICurrentUser _current;
    private readonly IPermissionService _permissions;

    public AdminController(UserManager<AppUser> users, AppDbContext db, ICurrentUser current, IPermissionService permissions)
    {
        _users = users;
        _db = db;
        _current = current;
        _permissions = permissions;
    }

    private async Task EnsureAdminAsync()
    {
        if (!await _permissions.IsOrganizationAdminAsync())
            throw new ForbiddenException("Solo un administrador de la organización puede hacer esto.");
    }

    private Guid OrgId => _current.OrganizationId ?? throw new ForbiddenException();

    public record CreateUserRequest(string FullName, string Email, string Password, Guid? RoleId);
    public record SetRoleRequest(Guid? RoleId);

    [HttpPost("users")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        await EnsureAdminAsync();
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password) || string.IsNullOrWhiteSpace(req.FullName))
            throw new ValidationException("Nombre, correo y contraseña son obligatorios.");
        if (req.Password.Length < 8)
            throw new ValidationException("La contraseña debe tener al menos 8 caracteres.");
        if (await _users.FindByEmailAsync(req.Email) is not null)
            throw new ValidationException("Ya existe un usuario con ese correo.");
        await ValidateRoleAsync(req.RoleId);

        await using var transaction = await _db.Database.BeginTransactionAsync();
        var user = new AppUser { UserName = req.Email, Email = req.Email, FullName = req.FullName, IsActive = true };
        var result = await _users.CreateAsync(user, req.Password);
        if (!result.Succeeded)
            throw new ValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        _db.OrganizationMembers.Add(new OrganizationMember
        {
            OrganizationId = OrgId,
            UserId = user.Id,
            RoleId = req.RoleId,
            Name = req.FullName,
            IsActive = true,
            JoinedAt = DateTime.UtcNow,
        });
        await _db.SaveChangesAsync();
        await transaction.CommitAsync();

        return Ok(new { userId = user.Id, email = user.Email, fullName = user.FullName });
    }

    [HttpPut("members/{userId:guid}/role")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetRole(Guid userId, [FromBody] SetRoleRequest req)
    {
        await EnsureAdminAsync();
        await ValidateRoleAsync(req.RoleId);
        var member = await _db.OrganizationMembers.FirstOrDefaultAsync(m => m.OrganizationId == OrgId && m.UserId == userId)
            ?? throw new NotFoundException("Miembro no encontrado.");
        member.RoleId = req.RoleId;
        await _db.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    private async Task ValidateRoleAsync(Guid? roleId)
    {
        if (roleId.HasValue && !await _db.OrganizationRoles.AsNoTracking().AnyAsync(r => r.Id == roleId && r.OrganizationId == OrgId))
            throw new ValidationException("El rol debe pertenecer a esta organización.");
    }
}
