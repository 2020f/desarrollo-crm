using Desarrollo.Domain.Common;

namespace Desarrollo.Domain.Files;

public enum FileVisibility { Context = 0, Private = 1, Public = 2 }

/// <summary>Metadatos del archivo almacenado (blob).</summary>
public class StoredFile : Entity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public string InternalName { get; set; } = string.Empty;
    public string OriginalName { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public Guid UploaderId { get; set; }
    public FileVisibility Visibility { get; set; } = FileVisibility.Private;
    public Guid? ProjectId { get; set; }
    public string? Description { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    public List<Attachment> Links { get; set; } = new();
}

/// <summary>Vínculo de un archivo a un contexto (tarea, documento, mensaje, ticket).</summary>
public class Attachment : Entity
{
    public Guid StoredFileId { get; set; }
    public StoredFile? StoredFile { get; set; }
    public Guid OrganizationId { get; set; }
    public string EntityType { get; set; } = string.Empty;
    public Guid EntityId { get; set; }
    public Guid LinkedBy { get; set; }
    public string? Description { get; set; }
}
