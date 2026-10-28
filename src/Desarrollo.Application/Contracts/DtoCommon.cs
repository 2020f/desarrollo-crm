namespace Desarrollo.Application.Contracts;

public record Paged<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public record UserRef(Guid Id, string Name, string? Avatar);

public record OrganizationDto(Guid Id, string Name, string Slug, string? TimeZoneId);

public record MemberDto(Guid Id, Guid UserId, string Name, bool IsActive, string? RoleName, string? Email = null, Guid? RoleId = null);

public record RoleDto(Guid Id, string Name, string Description, bool IsSystem, List<Desarrollo.Domain.Permission> Permissions);

public record ProductDto(Guid Id, string Name, string? Description, bool IsActive);

public record ComponentDto(Guid Id, Guid ProductId, string Name, string? Description);

public record ProjectDto(
    Guid Id, string Code, string Name, string? Objective,
    Desarrollo.Domain.ProjectStatus Status, Desarrollo.Domain.Priority Priority,
    Guid? LeaderId, DateTime? DueDate, int? Progress, bool IsPrivate);

public record EpicDto(Guid Id, string Title, string? Goal, Guid? OwnerId, Desarrollo.Domain.Priority Priority, DateTime? DueDate);

public record CycleDto(Guid Id, string Name, string? Goal, DateTime StartDate, DateTime EndDate, bool IsActive);

public record MilestoneDto(Guid Id, string Title, DateTime? DueDate);

public record ReleaseDto(Guid Id, string Name, DateTime? PlannedDate, DateTime? ReleasedAt, string? ReleaseNotes, string? Environments);

public record DeploymentDto(Guid Id, Guid ReleaseId, Desarrollo.Domain.DeploymentEnvironment Environment, Desarrollo.Domain.DeploymentResult Result, DateTime? FinishedAt, Guid? ActorId, bool IsManualConfirmation);

public record WorkflowStateDto(Guid Id, string Name, Desarrollo.Domain.WorkItemCategory Category, int Order, string? Color, bool IsDefault);

public record RepositoryRefDto(Guid Id, string RepoName, string Kind, string Ref, string Title, string? Url, string? Status);
