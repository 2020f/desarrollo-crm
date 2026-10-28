using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class OrganizationService : IOrganizationService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;

    public OrganizationService(AppDbContext db, ICurrentUser user)
    {
        _db = db;
        _user = user;
    }

    public async Task<OrganizationDto?> GetCurrentAsync()
    {
        if (_user.OrganizationId is null) return null;
        var org = await _db.Organizations.AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == _user.OrganizationId.Value);
        return org?.ToDto();
    }

    public async Task<List<MemberDto>> GetMembersAsync()
    {
        if (_user.OrganizationId is null) return new();
        var members = await _db.OrganizationMembers.AsNoTracking()
            .Where(m => m.OrganizationId == _user.OrganizationId.Value)
            .ToListAsync();
        var roles = await _db.OrganizationRoles.AsNoTracking()
            .Where(r => r.OrganizationId == _user.OrganizationId.Value)
            .ToDictionaryAsync(r => r.Id, r => r.Name);
        var userIds = members.Select(m => m.UserId).ToList();
        var emails = await _db.Users.Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.Email }).ToDictionaryAsync(u => u.Id, u => u.Email);
        return members.Select(m => new MemberDto(m.Id, m.UserId, m.Name, m.IsActive,
            m.RoleId.HasValue ? roles.GetValueOrDefault(m.RoleId.Value) : null,
            emails.GetValueOrDefault(m.UserId), m.RoleId)).ToList();
    }

    public async Task<List<RoleDto>> GetRolesAsync()
    {
        if (_user.OrganizationId is null) return new();
        var roles = await _db.OrganizationRoles.AsNoTracking()
            .Where(r => r.OrganizationId == _user.OrganizationId.Value)
            .ToListAsync();
        return roles.Select(r => new RoleDto(r.Id, r.Name, r.Description, r.IsSystem, r.Permissions)).ToList();
    }

    public async Task<List<ProductDto>> GetProductsAsync()
    {
        if (_user.OrganizationId is null) return new();
        var products = await _db.Products.AsNoTracking()
            .Where(p => p.OrganizationId == _user.OrganizationId.Value)
            .ToListAsync();
        return products.Select(p => p.ToDto()).ToList();
    }
}
