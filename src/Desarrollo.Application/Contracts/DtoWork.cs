namespace Desarrollo.Application.Contracts;

public record WorkItemCardDto(
    Guid Id, string Code, string Title, Desarrollo.Domain.WorkItemType Type,
    Guid? AssigneeId, string? AssigneeName,
    Desarrollo.Domain.Priority Priority, Guid StateId, string StateName, string? StateColor,
    DateTime? DueDate, bool IsBlocked, int? ChecklistProgress, List<string> Labels,
    Guid? ProjectId, string? ProjectCode, string RowVersion = "");

public record WorkItemDetailDto(
    Guid Id, string Code, string Title, Desarrollo.Domain.WorkItemType Type,
    string? Description, string? Objective, string? Context,
    Guid AuthorId, Guid? AssigneeId, string? AssigneeName,
    List<Guid> Collaborators, List<Guid> Reviewers, List<Guid> Followers,
    Desarrollo.Domain.Priority Priority, DateTime? StartDate, DateTime? DueDate, double? EstimateHours,
    Guid? CycleId, Guid? EpicId, Guid? ReleaseId,
    Guid StateId, string StateName, Desarrollo.Domain.WorkItemCategory Category,
    bool IsBlocked, string? BlockReason, Guid? BlockedByWorkItemId,
    string? AcceptanceCriteria, List<string> Labels,
    string? ReproSteps, string? ExpectedResult, string? ActualResult,
    string? Environment, string? AffectedVersion, Desarrollo.Domain.Severity? Severity,
    string? ResearchQuestion, string? ResearchConclusion,
    Guid? ProjectId, string? ProjectCode, Guid OrganizationId,
    DateTime CreatedAt, DateTime UpdatedAt,
    Guid? ParentTaskId = null, string? Notes = null, int? ProgressPercent = null, string? AccentColor = null,
    string RowVersion = "", string FullPath = "", bool IsArchived = false,
    IReadOnlyList<Desarrollo.Domain.Permission>? Permissions = null,
    bool CanEdit = false, bool CanCreateChild = false, bool CanArchive = false, bool CanChangeState = false);

public record ChecklistItemDto(Guid Id, string Text, bool IsCompleted, Guid? AssigneeId, Guid? CompletedBy, int Order);

public record ChecklistDto(Guid Id, string Title, int Order, List<ChecklistItemDto> Items);

public record CommentDto(Guid Id, Guid AuthorId, string? AuthorName, string Body, DateTime CreatedAt);

public record CustomFieldDto(Guid Id, string Name, Desarrollo.Domain.CustomFieldType Type, List<string> Options, bool IsRequired, bool IsActive);

public record CustomFieldValueDto(Guid CustomFieldId, string? Value);

public record SavedViewDto(Guid Id, string Name, Desarrollo.Domain.SavedViewType Type, string? RoleName, string? Config, bool IsDefault);

public record CreateWorkItemRequest(
    string Title, Desarrollo.Domain.WorkItemType Type, Guid? ProjectId,
    Guid? AssigneeId, Desarrollo.Domain.Priority Priority,
    string? Description, DateTime? DueDate, Guid? StateId,
    Guid? ParentTaskId = null, string? Notes = null, int? ProgressPercent = null, string? AccentColor = null,
    Guid? ClientRequestId = null);

public record UpdateWorkItemRequest(
    string Title, Desarrollo.Domain.WorkItemType Type,
    Guid? AssigneeId, Desarrollo.Domain.Priority Priority,
    string? Description, string? Objective, string? Context,
    DateTime? StartDate, DateTime? DueDate, double? EstimateHours,
    Guid? CycleId, Guid? EpicId, Guid? ReleaseId, Guid StateId,
    string? AcceptanceCriteria, List<string> Labels,
    string? ReproSteps, string? ExpectedResult, string? ActualResult,
    string? Environment, string? AffectedVersion, Desarrollo.Domain.Severity? Severity,
    string? ResearchQuestion, string? ResearchConclusion,
    Guid? ParentTaskId = null, string? Notes = null, int? ProgressPercent = null, string? AccentColor = null,
    string? RowVersion = null, bool ClearProgressPercent = false);

public record PatchWorkItemRequest(
    string? Title = null, string? Description = null, string? Notes = null,
    Desarrollo.Domain.Priority? Priority = null, Guid? StateId = null,
    int? ProgressPercent = null, string? AccentColor = null,
    string? RowVersion = null, bool ClearProgressPercent = false);

public record ArchiveWorkItemRequest(string RowVersion, bool IncludeDescendants);
public record ArchiveTaskDto(Guid Id, string Code, string Title);
public record ArchivePreviewDto(int Count, IReadOnlyList<ArchiveTaskDto> Tasks, bool CanArchive);
