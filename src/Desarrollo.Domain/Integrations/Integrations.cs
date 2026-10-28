using Desarrollo.Domain.Common;

namespace Desarrollo.Domain.Integrations;

public class RepositoryReference : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? WorkItemId { get; set; }
    public string RepoName { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Url { get; set; }
    public string? Status { get; set; }
    public string? Metadata { get; set; }
}

public class Deployment : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid ReleaseId { get; set; }
    public Projects.Release? Release { get; set; }
    public DeploymentEnvironment Environment { get; set; }
    public DeploymentResult Result { get; set; } = DeploymentResult.Pendiente;
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public Guid? ActorId { get; set; }
    public string? Evidence { get; set; }
    public bool IsManualConfirmation { get; set; }
}

public class AutomationRule : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public AutomationEventType EventType { get; set; }
    public string? Condition { get; set; }
    public AutomationActionType ActionType { get; set; }
    public string? ActionConfig { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Scope { get; set; } = string.Empty;
    public int Order { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public class AutomationExecution : Entity
{
    public Guid AutomationRuleId { get; set; }
    public AutomationRule? AutomationRule { get; set; }
    public DateTime TriggeredAt { get; set; }
    public string? EventPayload { get; set; }
    public bool Succeeded { get; set; }
    public string? Result { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
}

public class AuditLog : Entity
{
    public Guid OrganizationId { get; set; }
    public string ActorType { get; set; } = "User";
    public Guid? ActorId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public string? OldState { get; set; }
    public string? NewState { get; set; }
    public string? Reason { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
