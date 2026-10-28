using Desarrollo.Application.Contracts;
using Desarrollo.Domain;

namespace Desarrollo.Application.Abstractions;

public interface IPermissionService
{
    Task<bool> CanAccessProjectAsync(Guid projectId);
    Task EnsureProjectPermissionAsync(Guid projectId, Permission permission);
    Task<HashSet<Permission>> GetProjectPermissionsAsync(Guid projectId);
    Task<bool> IsOrganizationAdminAsync();
    Task<bool> CanReadWorkItemAsync(Guid workItemId);
    Task EnsureCanReadWorkItemAsync(Guid workItemId);
}

public interface IOrganizationService
{
    Task<OrganizationDto?> GetCurrentAsync();
    Task<List<MemberDto>> GetMembersAsync();
    Task<List<RoleDto>> GetRolesAsync();
    Task<List<ProductDto>> GetProductsAsync();
}

public interface IProjectService
{
    Task<Paged<ProjectDto>> ListAsync(ProjectListQuery query);
    Task<ProjectDetailDto> GetAsync(Guid id);
    Task<ProjectDto> CreateAsync(CreateProjectRequest req);
    Task UpdateAsync(Guid id, UpdateProjectRequest req);
    Task<List<WorkflowStateDto>> GetStatesAsync(Guid projectId);
    Task<List<EpicDto>> GetEpicsAsync(Guid projectId);
    Task<List<CycleDto>> GetCyclesAsync(Guid projectId);
    Task<List<ReleaseDto>> GetReleasesAsync(Guid projectId);
    Task<List<MemberDto>> GetProjectMembersAsync(Guid projectId);
    Task<MemberDto> AddMemberAsync(Guid projectId, Guid userId, Guid? roleId);
    Task RemoveMemberAsync(Guid projectId, Guid userId);
    Task UpdateMemberRoleAsync(Guid projectId, Guid userId, Guid? roleId);
}

public record ProjectListQuery(string? Search, ProjectStatus? Status, int Page = 1, int PageSize = 50);

public record CreateProjectRequest(string Name, string Code, string? Objective, Guid? ProductId, Guid? LeaderId, DateTime? DueDate, Priority Priority);

public record UpdateProjectRequest(string Name, string? Objective, string? Scope, Guid? LeaderId, ProjectStatus Status, Priority Priority, DateTime? StartDate, DateTime? DueDate, string? Risks);

public record ProjectDetailDto(
    ProjectDto Summary, string? Scope, DateTime? StartDate, string? Risks, string? Links,
    List<MemberDto> Members, List<EpicDto> Epics, List<CycleDto> Cycles, List<MilestoneDto> Milestones, List<ReleaseDto> Releases);

public interface IWorkItemService
{
    Task<Paged<WorkItemCardDto>> QueryAsync(WorkItemQuery query);
    Task<WorkItemDetailDto> GetAsync(Guid id);
    Task<WorkItemDetailDto> CreateAsync(CreateWorkItemRequest req);
    Task<WorkItemDetailDto> CreateChildAsync(Guid parentId, CreateWorkItemRequest req);
    Task<WorkItemDetailDto> UpdateAsync(Guid id, UpdateWorkItemRequest req);
    Task<WorkItemDetailDto> PatchAsync(Guid id, PatchWorkItemRequest req);
    Task<WorkItemDetailDto> ChangeParentAsync(Guid id, Guid? parentId, string? rowVersion = null);
    Task<ArchivePreviewDto> ArchivePreviewAsync(Guid id, bool restore = false);
    Task<WorkItemDetailDto> ArchiveAsync(Guid id, ArchiveWorkItemRequest req, bool restore = false);
    Task<WorkItemDetailDto> TransitionAsync(Guid id, Guid toStateId, string? reason, string? rowVersion = null);
    Task<CommentDto> AddCommentAsync(Guid id, string body);
    Task<List<CommentDto>> GetCommentsAsync(Guid id);
    Task<List<ChecklistDto>> GetChecklistsAsync(Guid id);
    Task<ChecklistDto> AddChecklistAsync(Guid workItemId, string title);
    Task<ChecklistItemDto> AddChecklistItemAsync(Guid checklistId, string text, Guid? assigneeId);
    Task ToggleChecklistItemAsync(Guid itemId);
    Task SetBlockedAsync(Guid id, bool blocked, string? reason);
    Task AddRelationAsync(Guid id, Guid targetId, WorkRelationType type);
    Task<List<WorkflowStateDto>> GetAvailableTransitionsAsync(Guid id);
}

public interface ITaskDiagramService
{
    Task<TaskDiagramDto> GetAsync(Guid projectId, bool includeArchived = false);
    Task<List<DiagramPositionDto>> SavePositionsAsync(Guid projectId, List<DiagramPositionDto> positions);
    Task SaveViewportAsync(Guid projectId, SaveViewportRequest viewport);
}

public record WorkItemQuery(
    Guid? ProjectId, string? Search, Guid? AssigneeId, Priority? Priority,
    WorkItemType? Type, Guid? StateId, bool? Blocked, Guid? EpicId, Guid? CycleId,
    bool? Overdue, string? Label, string? Sort, int Page = 1, int PageSize = 100, bool IncludeArchived = false);

public interface INotificationService
{
    Task<Paged<NotificationDto>> ListAsync(bool unreadOnly, int page, int pageSize);
    Task<int> UnreadCountAsync();
    Task MarkReadAsync(Guid id);
    Task MarkAllReadAsync();
}

public interface ITicketService
{
    Task<Paged<TicketDto>> ListAsync(TicketQuery query);
    Task<TicketDto> CreateAsync(CreateTicketRequest req);
    Task<TicketDto> GetAsync(Guid id);
    Task<TicketDto> SetStatusAsync(Guid id, TicketStatus status, string? reason);
    Task<TicketDto> AssignAsync(Guid id, Guid? assigneeId);
    Task<TicketCommentDto> AddCommentAsync(Guid id, string body, bool isInternal);
    Task<List<TicketCommentDto>> GetCommentsAsync(Guid id);
    Task LinkWorkItemAsync(Guid id, Guid workItemId);
}

public record TicketQuery(Guid? ProjectId, TicketStatus? Status, string? Search, int Page = 1, int PageSize = 50);

public interface IChannelService
{
    Task<List<ChannelDto>> ListAsync(Guid? projectId);
    Task<ChannelDto> CreateAsync(Guid? projectId, string name, string? topic);
    Task<ChannelDto> GetOrCreateDirectAsync(Guid userId);
    Task<Paged<MessageDto>> GetMessagesAsync(Guid channelId, int page, int pageSize);
    Task<MessageDto> SendAsync(Guid channelId, string body, Guid? threadRootId);
    Task<MessageDto> EditAsync(Guid messageId, string body);
    Task MarkReadAsync(Guid channelId);
}

public interface IDocumentService
{
    Task<List<DocumentDto>> ListAsync(Guid projectId);
    Task<DocumentDto> GetAsync(Guid id);
    Task<DocumentDto> CreateAsync(Guid projectId, string title, Guid? parentId, string content,
        Desarrollo.Domain.Docs.DocumentVisibility visibility = Desarrollo.Domain.Docs.DocumentVisibility.Privada,
        IReadOnlyList<Guid>? readerIds = null, string contentFormat = "plain");
    Task<DocumentDto> UpdateAsync(Guid id, string title, string content, string? note,
        Desarrollo.Domain.Docs.DocumentVisibility? visibility = null, IReadOnlyList<Guid>? readerIds = null,
        string? contentFormat = null, string? rowVersion = null);
    Task<List<DocumentVersionDto>> GetVersionsAsync(Guid id);
}

public interface ICalendarService
{
    Task<List<CalendarEventDto>> GetRangeAsync(DateTime start, DateTime end, string? timeZoneId = null);
    Task<CalendarEventDto> CreateAsync(CreateCalendarEventRequest req);
    Task<CalendarEventDto> UpdateAsync(Guid id, CreateCalendarEventRequest req);
    Task DeleteAsync(Guid id);
}

public record CreateCalendarEventRequest(
    string Title, string? Description, CalendarEventType Type, bool IsAllDay,
    DateTime Start, DateTime End, string? TimeZoneId, Guid? ProjectId,
    RecurrenceFrequency? Recurrence, Guid? LinkedWorkItemId, Guid? LinkedMilestoneId, Guid? LinkedReleaseId,
    Priority? Priority, string? Color, Guid? AssigneeId);

public interface IReportService
{
    Task<ReportSnapshotDto> GetProjectSnapshotAsync(Guid projectId);
}

public interface IBackgroundJobService
{
    Task<string> EnqueueAsync(string kind, string payload);
}
