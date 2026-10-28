using Desarrollo.Domain.Common;

namespace Desarrollo.Domain.Organizations;

public class Product : Entity
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
    public List<Component> Components { get; set; } = new();
}

public class Component : Entity
{
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}
