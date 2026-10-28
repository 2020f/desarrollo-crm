namespace Desarrollo.Application.Contracts;

public record DiagramTaskDto(
    Guid Id, string Code, string Title, string DisplayTitle, Guid? ParentTaskId,
    string? Description, string? Notes, Desarrollo.Domain.Priority Priority,
    Guid StateId, string StateName, string? StateColor,
    int? ProgressPercent, string? AccentColor, int Depth, double X, double Y,
    string RowVersion, string? PositionRowVersion, int? ChecklistProgress, string FullPath, bool IsArchived,
    bool CanEdit, bool CanCreateChild, bool CanArchive, bool CanChangeState,
    Desarrollo.Domain.WorkItemType Type, Guid? AssigneeId, string? AssigneeName);

public record TaskDiagramDto(
    IReadOnlyList<DiagramTaskDto> Tasks, double ViewportX, double ViewportY, double Zoom,
    string ProjectName, IReadOnlyList<Desarrollo.Domain.Permission> Permissions,
    IReadOnlyList<WorkflowStateDto> States, IReadOnlyList<Guid> CollapsedTaskIds);

public record DiagramPositionDto(Guid TaskId, double X, double Y, string? RowVersion = null);

public record SavePositionsRequest(List<DiagramPositionDto> Positions);

public record SaveViewportRequest(double ViewportX, double ViewportY, double Zoom, List<Guid>? CollapsedTaskIds = null);

public record ChangeParentRequest(Guid? ParentTaskId, string? RowVersion = null);
