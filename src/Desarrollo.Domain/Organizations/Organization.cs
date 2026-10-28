using Desarrollo.Domain.Common;

namespace Desarrollo.Domain.Organizations;

public class Organization : Entity
{
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? TimeZoneId { get; set; }
    public List<OrganizationMember> Members { get; set; } = new();
    public List<Role> Roles { get; set; } = new();
    public List<Product> Products { get; set; } = new();
}

public class OrganizationMember : Entity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid UserId { get; set; }
    public Guid? RoleId { get; set; }
    public Role? Role { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
    public DateTime? JoinedAt { get; set; }
    public DateTime? LeftAt { get; set; }
}

public class Role : Entity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsSystem { get; set; }
    public List<Permission> Permissions { get; set; } = new();
}
