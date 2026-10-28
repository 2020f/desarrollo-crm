namespace Desarrollo.Application.Contracts;

public record FileDto(Guid Id, Guid StoredFileId, string Code, string Name, string ContentType,
    long SizeBytes, string? Description, Guid? ProjectId, string Visibility, Guid UploaderId,
    DateTime CreatedAt, bool CanDelete, string? EntityType = null, Guid? EntityId = null);
