using Desarrollo.Domain.Common;
using Desarrollo.Domain.Integrations;
using Desarrollo.Domain.Organizations;

namespace Desarrollo.Domain.Projects;

public class Project : Entity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Organization? Organization { get; set; }
    public Guid? ProductId { get; set; }
    public Product? Product { get; set; }
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Objective { get; set; }
    public string? Scope { get; set; }
    public Guid? LeaderId { get; set; }
    public ProjectStatus Status { get; set; } = ProjectStatus.Propuesto;
    public Priority Priority { get; set; } = Priority.Media;
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public string? Risks { get; set; }
    public string? Links { get; set; }
    public bool IsPrivate { get; set; } = true;
    public int WorkItemSequence { get; set; } = 1;

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<ProjectMember> Members { get; set; } = new();
    public List<Epic> Epics { get; set; } = new();
    public List<Cycle> Cycles { get; set; } = new();
    public List<Milestone> Milestones { get; set; } = new();
    public List<Release> Releases { get; set; } = new();
    public List<ProjectDependency> Dependencies { get; set; } = new();
}

public class ProjectMember : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid OrganizationMemberId { get; set; }
    public Guid UserId { get; set; }
    public Guid? RoleId { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}

public class ProjectDependency : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid DependsOnProjectId { get; set; }
    public string? Note { get; set; }
}

public class Epic : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Goal { get; set; }
    public Guid? OwnerId { get; set; }
    public Priority Priority { get; set; } = Priority.Media;
    public DateTime? DueDate { get; set; }
}

public class Cycle : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Goal { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Milestone : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime? DueDate { get; set; }
}

public class Release : Entity
{
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Scope { get; set; }
    public DateTime? PlannedDate { get; set; }
    public DateTime? ReleasedAt { get; set; }
    public string? Acceptance { get; set; }
    public string? Environments { get; set; }
    public string? ReleaseNotes { get; set; }
    public List<Deployment> Deployments { get; set; } = new();
}
