using Desarrollo.Domain.Common;
using Desarrollo.Domain.Projects;

namespace Desarrollo.Domain.Docs;

public enum DocumentVisibility { PublicaProyecto = 0, PersonasSeleccionadas = 1, Privada = 2 }

public class Document : Entity, ISoftDelete
{
    public Guid OrganizationId { get; set; }
    public Guid ProjectId { get; set; }
    public Project? Project { get; set; }
    public Guid? ParentId { get; set; }
    public Document? Parent { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string ContentFormat { get; set; } = "plain";
    public DocumentVisibility Visibility { get; set; } = DocumentVisibility.Privada;
    public Guid AuthorId { get; set; }
    public Guid? LastEditorId { get; set; }
    public List<string> Tags { get; set; } = new();
    public int? FavoriteById { get; set; }
    public bool IsArchived { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    public List<Document> Children { get; set; } = new();
    public List<DocumentVersion> Versions { get; set; } = new();
    public List<DocumentReader> Readers { get; set; } = new();
}

public class DocumentVersion : Entity
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }
    public string Content { get; set; } = string.Empty;
    public string ContentFormat { get; set; } = "plain";
    public Guid AuthorId { get; set; }
    public string? Note { get; set; }
}

public class DocumentReader : Entity
{
    public Guid DocumentId { get; set; }
    public Document? Document { get; set; }
    public Guid UserId { get; set; }
}
