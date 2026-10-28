using System.Data;
using System.Net.Mail;
using Desarrollo.Application.Common;
using Desarrollo.Domain.Integrations;
using Desarrollo.Domain.Organizations;
using Desarrollo.Infrastructure.Data;
using Desarrollo.Infrastructure.Identity;
using Desarrollo.Web.Seed;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Web.Services;

public record TeamAccountRequest(string FullName, string Email, string Password, string OrganizationName);
public record ProvisionedAccount(AppUser User, Organization Organization);
public sealed record RegistrationPolicy(bool Enabled);

/// <summary>One transactional path for explicit installation bootstrap and optional public registration.</summary>
public sealed class AccountProvisioningService(AppDbContext db, UserManager<AppUser> users)
{
    public async Task<ProvisionedAccount> CreateTeamAsync(TeamAccountRequest request, bool onlyIfEmpty = false)
    {
        var name = Required(request.FullName, 200, "Nombre");
        var organizationName = Required(request.OrganizationName, 200, "Organización");
        var email = Required(request.Email, 256, "Correo");
        if (!MailAddress.TryCreate(email, out var address) || address.Address != email)
            throw new ValidationException("El correo no es válido.");
        if (string.IsNullOrEmpty(request.Password) || request.Password.Length > 256)
            throw new ValidationException("La contraseña debe tener entre 8 y 256 caracteres e incluir un número.");
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        // Serialize first-install attempts and optional concurrent registrations within this database.
        await db.Database.ExecuteSqlRawAsync("DECLARE @result int; EXEC @result = sys.sp_getapplock @Resource=N'Desarrollo.AccountProvisioning', @LockMode='Exclusive', @LockOwner='Transaction', @LockTimeout=10000; IF @result<0 THROW 51001, 'Account initialization lock unavailable', 1;");
        if (onlyIfEmpty && (await db.Users.AnyAsync() || await db.Organizations.AnyAsync()))
            throw new InvalidStateException("La instalación ya tiene cuentas o equipos. El inicio administrativo no modifica cuentas existentes.");
        if (await users.FindByEmailAsync(email) is not null) throw new ValidationException("Ya existe una cuenta con ese correo.");
        var user = new AppUser { UserName = email, Email = email, FullName = name, IsActive = true, TimeZoneId = "America/Santo_Domingo" };
        var created = await users.CreateAsync(user, request.Password);
        if (!created.Succeeded) throw new ValidationException(string.Join(" ", created.Errors.Select(e => e.Description)));
        var slug = new string(organizationName.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray());
        slug = string.Join('-', slug.Split('-', StringSplitOptions.RemoveEmptyEntries));
        if (slug.Length > 80) slug = slug[..80];
        var organization = new Organization { Name = organizationName, Slug = $"{slug}-{Guid.NewGuid():N}", TimeZoneId = "America/Santo_Domingo" };
        db.Organizations.Add(organization);
        Role? adminRole = null;
        foreach (var definition in SystemRoles.Definitions)
        {
            var role = new Role { OrganizationId = organization.Id, Name = definition.Name,
                Description = definition.Description, IsSystem = true, Permissions = definition.Permissions.ToList() };
            db.OrganizationRoles.Add(role);
            if (definition.Name.StartsWith("Administrador", StringComparison.Ordinal)) adminRole = role;
        }
        db.OrganizationMembers.Add(new OrganizationMember { OrganizationId = organization.Id, UserId = user.Id,
            RoleId = adminRole!.Id, Name = user.FullName, IsActive = true, JoinedAt = DateTime.UtcNow });
        db.AuditLogs.Add(new AuditLog { OrganizationId = organization.Id, ActorId = user.Id, EntityType = "Organization",
            EntityId = organization.Id, Action = onlyIfEmpty ? "BootstrapOrganization" : "RegisterOrganization", CreatedBy = user.Id });
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
        return new(user, organization);
    }
    private static string Required(string value, int limit, string label)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > limit || value.Any(char.IsControl))
            throw new ValidationException($"{label} es obligatorio y admite hasta {limit} caracteres.");
        return value.Trim();
    }
}
