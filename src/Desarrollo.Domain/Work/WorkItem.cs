using Desarrollo.Domain.Common;
using Desarrollo.Domain.Organizations;
using Desarrollo.Domain.Projects;

namespace Desarrollo.Domain.Work;

public class WorkItem : Entity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? ProductId { get; set; }
    public Guid? ComponentId { get; set; }

    public int Sequence { get; set; }
    public string Code { get; set; } = string.Empty;

    public WorkItemType Type { get; set; } = WorkItemType.Funcionalidad;
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? Objective { get; set; }
    public string? Context { get; set; }

    public Guid AuthorId { get; set; }
    public Guid? AssigneeId { get; set; }
    public List<Guid> Collaborators { get; set; } = new();
    public List<Guid> Reviewers { get; set; } = new();
    public List<Guid> Followers { get; set; } = new();

    public Priority Priority { get; set; } = Priority.Media;
    public DateTime? StartDate { get; set; }
    public DateTime? DueDate { get; set; }
    public double? EstimateHours { get; set; }

    public Guid? CycleId { get; set; }
    public Guid? EpicId { get; set; }
    public Epic? Epic { get; set; }
    public Guid? ReleaseId { get; set; }
    public Release? Release { get; set; }

    public Guid StateId { get; set; }
    public WorkflowState? State { get; set; }
    public WorkItemCategory Category { get; set; } = WorkItemCategory.Pendiente;

    public Guid? ParentTaskId { get; set; }
    public WorkItem? Parent { get; set; }
    public List<WorkItem> Children { get; set; } = new();

    public string? Notes { get; set; }
    public int? ProgressPercent { get; set; }
    public string? AccentColor { get; set; }
    public Guid? CreationRequestId { get; set; }

    public bool IsBlocked { get; set; }
    public string? BlockReason { get; set; }
    public Guid? BlockedByWorkItemId { get; set; }
    public DateTime? BlockedAt { get; set; }
    public Guid? BlockResolverId { get; set; }
    public DateTime? BlockResolvedAt { get; set; }

    public string? AcceptanceCriteria { get; set; }
    public List<string> Labels { get; set; } = new();

    // Campos de tipos específicos (error, investigación)
    public string? ReproSteps { get; set; }
    public string? ExpectedResult { get; set; }
    public string? ActualResult { get; set; }
    public string? Environment { get; set; }
    public string? AffectedVersion { get; set; }
    public Severity? Severity { get; set; }
    public string? ResearchQuestion { get; set; }
    public string? ResearchConclusion { get; set; }

    public bool IsArchived { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<WorkRelation> OutgoingRelations { get; set; } = new();
    public List<WorkRelation> IncomingRelations { get; set; } = new();
    public List<Checklist> Checklists { get; set; } = new();
    public List<WorkItemComment> Comments { get; set; } = new();
    public List<WorkItemCustomFieldValue> CustomFieldValues { get; set; } = new();
}

public class WorkRelation : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid SourceId { get; set; }
    public WorkItem? Source { get; set; }
    public Guid TargetId { get; set; }
    public WorkItem? Target { get; set; }
    public WorkRelationType Type { get; set; }
}

public class WorkflowState : Entity
{
    public Guid? OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public WorkItemCategory Category { get; set; }
    public int Order { get; set; }
    public string? Color { get; set; }
    public bool IsDefault { get; set; }
    public bool IsArchived { get; set; }
}

public class Transition : Entity
{
    public Guid? OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid FromStateId { get; set; }
    public WorkflowState? FromState { get; set; }
    public Guid ToStateId { get; set; }
    public WorkflowState? ToState { get; set; }
    public string Name { get; set; } = string.Empty;
    public List<Permission> RequiredPermissions { get; set; } = new();
    public string? Conditions { get; set; }
}

public class Checklist : Entity
{
    public Guid WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Order { get; set; }
    public List<ChecklistItem> Items { get; set; } = new();
}

public class ChecklistItem : Entity
{
    public Guid ChecklistId { get; set; }
    public Checklist? Checklist { get; set; }
    public string Text { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public Guid? AssigneeId { get; set; }
    public Guid? CompletedBy { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int Order { get; set; }
    public Guid? SourceWorkItemId { get; set; }
}

public class WorkItemComment : Entity
{
    public Guid WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }
    public Guid AuthorId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
}

public class CustomField : Entity
{
    public Guid OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public string Name { get; set; } = string.Empty;
    public CustomFieldType Type { get; set; }
    public List<string> Options { get; set; } = new();
    public bool IsRequired { get; set; }
    public bool ReadPermission { get; set; } = true;
    public bool EditPermission { get; set; } = true;
    public bool IsActive { get; set; } = true;
}

public class WorkItemCustomFieldValue : Entity
{
    public Guid WorkItemId { get; set; }
    public WorkItem? WorkItem { get; set; }
    public Guid CustomFieldId { get; set; }
    public CustomField? CustomField { get; set; }
    public string? Value { get; set; }
}

public class SavedView : Entity
{
    public Guid? OrganizationId { get; set; }
    public Guid? ProjectId { get; set; }
    public Guid? OwnerId { get; set; }
    public SavedViewType Type { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? RoleName { get; set; }
    public string? Config { get; set; }
    public bool IsDefault { get; set; }
}
