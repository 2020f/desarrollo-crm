using System.Text.Json;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Docs;
using Desarrollo.Domain.Integrations;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

public class DocumentService : IDocumentService
{
    private readonly AppDbContext _db;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;

    public DocumentService(AppDbContext db, ICurrentUser user, IPermissionService permissions)
    { _db = db; _user = user; _permissions = permissions; }

    private Guid OrgId => _user.OrganizationId ?? throw new ForbiddenException();
    private Guid UserId => _user.UserId ?? throw new ForbiddenException();

    public async Task<List<DocumentDto>> ListAsync(Guid projectId)
    {
        var permissions = await RequireProjectAsync(projectId);
        var edit = permissions.Contains(Permission.Editar);
        var authorEdit = edit || permissions.Contains(Permission.Crear);
        // Project only metadata: long/private content is never selected for the tree.
        var docs = await Readable(projectId).OrderBy(d => d.Title).Select(d => new DocumentDto(
            d.Id, d.Title, d.ParentId, d.AuthorId, d.Tags, d.UpdatedAt, null, d.ProjectId, d.Visibility,
            d.Readers.Select(r => r.UserId).ToList(), edit || (d.AuthorId == UserId && authorEdit),
            d.AuthorId == UserId && authorEdit, d.ContentFormat, Convert.ToBase64String(d.RowVersion))).ToListAsync();
        var ids = docs.Select(d => d.Id).ToHashSet();
        return docs.Select(d => d.ParentId.HasValue && !ids.Contains(d.ParentId.Value) ? d with { ParentId = null } : d).ToList();
    }

    public async Task<DocumentDto> GetAsync(Guid id)
    {
        var (doc, permissions) = await LoadReadableAsync(id);
        return await DetailAsync(doc, permissions);
    }

    public async Task<DocumentDto> CreateAsync(Guid projectId, string title, Guid? parentId, string content,
        DocumentVisibility visibility = DocumentVisibility.Privada, IReadOnlyList<Guid>? readerIds = null, string contentFormat = "plain")
    {
        var permissions = await RequireProjectAsync(projectId);
        if (!permissions.Contains(Permission.Crear)) throw new ForbiddenException();
        title = WorkItemMutationRules.Title(title);
        var validated = DocumentContentRules.Validate(content, contentFormat);
        WorkItemMutationRules.EnumValue(visibility, "Visibilidad");
        var readers = NormalizeReaders(visibility, readerIds ?? Array.Empty<Guid>(), UserId);
        await ValidateReadersAsync(projectId, readers);
        if (parentId.HasValue && !await Readable(projectId).AnyAsync(d => d.Id == parentId))
            throw new ValidationException("La página madre debe ser accesible y pertenecer al mismo proyecto.");
        var doc = new Document
        {
            OrganizationId = OrgId, ProjectId = projectId, Title = title, ParentId = parentId,
            Content = validated.Content, ContentFormat = validated.Format, Visibility = visibility,
            AuthorId = UserId, LastEditorId = UserId, CreatedBy = UserId, UpdatedBy = UserId,
        };
        foreach (var readerId in readers) doc.Readers.Add(new DocumentReader { DocumentId = doc.Id, UserId = readerId, CreatedBy = UserId });
        _db.Documents.Add(doc);
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = doc.Id, Document = doc, Content = doc.Content,
            ContentFormat = doc.ContentFormat, AuthorId = UserId, CreatedBy = UserId });
        AuditAccess(doc, "CreateDocument", null, new { doc.Visibility, ReaderIds = readers });
        await SaveAsync();
        return await GetAsync(doc.Id);
    }

    public async Task<DocumentDto> UpdateAsync(Guid id, string title, string content, string? note,
        DocumentVisibility? visibility = null, IReadOnlyList<Guid>? readerIds = null, string? contentFormat = null, string? rowVersion = null)
    {
        await EnsureActiveAsync();
        var doc = await _db.Documents.FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == OrgId && !d.IsArchived)
            ?? throw new NotFoundException("Documento no encontrado.");
        await _db.Entry(doc).ReloadAsync();
        if (doc.IsArchived || doc.IsDeleted) throw new NotFoundException("Documento no encontrado.");
        var permissions = await RequireProjectAsync(doc.ProjectId);
        var currentReaders = await _db.DocumentReaders.AsNoTracking().Where(r => r.DocumentId == id).Select(r => r.UserId).ToListAsync();
        if (!CanRead(doc, currentReaders) || !CanEdit(doc, permissions)) throw new ForbiddenException();
        WorkItemMutationRules.RequireVersion(doc, rowVersion);
        title = WorkItemMutationRules.Title(title);
        var validated = DocumentContentRules.Validate(content, contentFormat ?? doc.ContentFormat);
        note = WorkItemMutationRules.Text(note, 2000, "La nota de versión");
        var targetVisibility = visibility ?? doc.Visibility;
        WorkItemMutationRules.EnumValue(targetVisibility, "Visibilidad");
        var readers = NormalizeReaders(targetVisibility, readerIds ?? (targetVisibility == doc.Visibility ? currentReaders : Array.Empty<Guid>()), doc.AuthorId);
        var accessChanged = targetVisibility != doc.Visibility || !readers.Order().SequenceEqual(currentReaders.Order());
        if (accessChanged)
        {
            if (doc.AuthorId != UserId) throw new ForbiddenException("Solo la persona autora puede cambiar la audiencia.");
            await ValidateReadersAsync(doc.ProjectId, readers);
        }
        if (doc.Content != validated.Content || doc.ContentFormat != validated.Format || note is not null)
            _db.DocumentVersions.Add(new DocumentVersion { DocumentId = id, Content = validated.Content,
                ContentFormat = validated.Format, AuthorId = UserId, Note = note, CreatedBy = UserId });
        if (accessChanged)
        {
            var existing = await _db.DocumentReaders.Where(r => r.DocumentId == id).ToListAsync();
            _db.DocumentReaders.RemoveRange(existing.Where(r => !readers.Contains(r.UserId)));
            foreach (var readerId in readers.Where(userId => !currentReaders.Contains(userId)))
                _db.DocumentReaders.Add(new DocumentReader { DocumentId = id, UserId = readerId, CreatedBy = UserId });
            AuditAccess(doc, "ChangeAccess", new { doc.Visibility, ReaderIds = currentReaders.Order().ToList() },
                new { Visibility = targetVisibility, ReaderIds = readers.Order().ToList() });
        }
        doc.Title = title; doc.Content = validated.Content; doc.ContentFormat = validated.Format;
        doc.Visibility = targetVisibility; doc.LastEditorId = UserId; doc.UpdatedBy = UserId; doc.UpdatedAt = DateTime.UtcNow;
        await SaveAsync();
        return await GetAsync(id);
    }

    public async Task<List<DocumentVersionDto>> GetVersionsAsync(Guid id)
    {
        await LoadReadableAsync(id);
        var versions = await _db.DocumentVersions.AsNoTracking().Where(v => v.DocumentId == id)
            .OrderByDescending(v => v.CreatedAt).ThenByDescending(v => v.Id).ToListAsync();
        return versions.Select(v => v.ToDto()).ToList();
    }

    private IQueryable<Document> Readable(Guid projectId) => _db.Documents.AsNoTracking()
        .Where(d => d.OrganizationId == OrgId && d.ProjectId == projectId && !d.IsArchived
            && (d.AuthorId == UserId || d.Visibility == DocumentVisibility.PublicaProyecto
                || (d.Visibility == DocumentVisibility.PersonasSeleccionadas && d.Readers.Any(r => r.UserId == UserId))));

    private async Task<(Document Doc, HashSet<Permission> Permissions)> LoadReadableAsync(Guid id)
    {
        await EnsureActiveAsync();
        var doc = await _db.Documents.AsNoTracking().Include(d => d.Readers)
            .FirstOrDefaultAsync(d => d.Id == id && d.OrganizationId == OrgId && !d.IsArchived)
            ?? throw new NotFoundException("Documento no encontrado.");
        var permissions = await RequireProjectAsync(doc.ProjectId);
        if (!CanRead(doc, doc.Readers.Select(r => r.UserId))) throw new ForbiddenException("No tienes acceso a este documento.");
        return (doc, permissions);
    }

    private bool CanRead(Document doc, IEnumerable<Guid> readers) => doc.AuthorId == UserId
        || doc.Visibility == DocumentVisibility.PublicaProyecto
        || (doc.Visibility == DocumentVisibility.PersonasSeleccionadas && readers.Contains(UserId));

    private bool CanEdit(Document doc, HashSet<Permission> permissions) => permissions.Contains(Permission.Editar)
        || (doc.AuthorId == UserId && permissions.Contains(Permission.Crear));

    private async Task<DocumentDto> DetailAsync(Document doc, HashSet<Permission> permissions)
    {
        var edit = CanEdit(doc, permissions);
        var dto = doc.ToDto(canEdit: edit, canManageAccess: doc.AuthorId == UserId && edit);
        if (doc.ParentId.HasValue && !await Readable(doc.ProjectId).AnyAsync(parent => parent.Id == doc.ParentId))
            dto = dto with { ParentId = null };
        return dto;
    }

    private async Task EnsureActiveAsync()
    {
        if (!await _db.Users.AsNoTracking().AnyAsync(u => u.Id == UserId && u.IsActive)
            || !await _db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == OrgId && m.UserId == UserId && m.IsActive))
            throw new ForbiddenException("Necesitas una membresía activa para acceder a los documentos.");
    }

    private async Task<HashSet<Permission>> RequireProjectAsync(Guid projectId)
    {
        await EnsureActiveAsync();
        if (!await _db.Projects.AsNoTracking().AnyAsync(p => p.Id == projectId && p.OrganizationId == OrgId && p.Status != ProjectStatus.Archivado))
            throw new ForbiddenException();
        var permissions = await _permissions.GetProjectPermissionsAsync(projectId);
        if (!permissions.Contains(Permission.Leer)) throw new ForbiddenException();
        return permissions;
    }

    private static List<Guid> NormalizeReaders(DocumentVisibility visibility, IReadOnlyList<Guid> readerIds, Guid authorId)
    {
        if (readerIds.Count > 200 || readerIds.Any(id => id == Guid.Empty)) throw new ValidationException("La audiencia admite hasta 200 personas válidas.");
        var result = readerIds.Where(id => id != authorId).Distinct().Order().ToList();
        if (visibility != DocumentVisibility.PersonasSeleccionadas && result.Count != 0)
            throw new ValidationException("Solo los documentos para personas seleccionadas tienen una lista de lectores.");
        if (visibility == DocumentVisibility.PersonasSeleccionadas && result.Count == 0)
            throw new ValidationException("Selecciona al menos una persona con acceso al proyecto.");
        return result;
    }

    private async Task ValidateReadersAsync(Guid projectId, IReadOnlyList<Guid> readerIds)
    {
        if (readerIds.Count == 0) return;
        var project = await _db.Projects.AsNoTracking().FirstAsync(p => p.Id == projectId && p.OrganizationId == OrgId);
        var members = await _db.OrganizationMembers.AsNoTracking().Where(m => m.OrganizationId == OrgId && m.IsActive
            && readerIds.Contains(m.UserId) && _db.Users.Any(u => u.Id == m.UserId && u.IsActive)).ToListAsync();
        if (members.Select(m => m.UserId).Distinct().Count() != readerIds.Count)
            throw new ValidationException("Cada lector debe ser un miembro activo de esta organización.");
        var projectMembers = await _db.ProjectMembers.AsNoTracking().Where(m => m.ProjectId == projectId && m.IsActive && readerIds.Contains(m.UserId)).ToListAsync();
        var roles = await _db.OrganizationRoles.AsNoTracking().Where(r => r.OrganizationId == OrgId).ToDictionaryAsync(r => r.Id);
        foreach (var member in members)
        {
            var organizationRole = member.RoleId.HasValue ? roles.GetValueOrDefault(member.RoleId.Value) : null;
            if (organizationRole is not null && Enum.GetValues<Permission>().All(organizationRole.Permissions.Contains)) continue;
            var projectMember = projectMembers.FirstOrDefault(m => m.UserId == member.UserId);
            if (projectMember is null)
            {
                if (!project.IsPrivate) continue;
            }
            else if (!projectMember.RoleId.HasValue) continue;
            else if (roles.GetValueOrDefault(projectMember.RoleId.Value)?.Permissions.Contains(Permission.Leer) == true) continue;
            throw new ValidationException("Cada lector debe tener permiso de lectura en el proyecto.");
        }
    }

    private void AuditAccess(Document doc, string action, object? before, object after) => _db.AuditLogs.Add(new AuditLog
    {
        OrganizationId = OrgId, ActorId = UserId, Action = action, EntityType = "Document", EntityId = doc.Id,
        OldState = before is null ? null : JsonSerializer.Serialize(before), NewState = JsonSerializer.Serialize(after), CreatedBy = UserId,
    });

    private async Task SaveAsync()
    {
        try { await _db.SaveChangesAsync(); }
        catch (DbUpdateConcurrencyException) { throw new ConcurrencyConflictException(); }
    }
}
