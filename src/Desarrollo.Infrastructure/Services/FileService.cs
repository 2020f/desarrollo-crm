using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Application.Contracts;
using Desarrollo.Domain;
using Desarrollo.Domain.Files;
using Desarrollo.Domain.Integrations;
using Desarrollo.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Infrastructure.Services;

/// <summary>Folder visibility is an authorization rule, never a property of the download URL.</summary>
public sealed class FileService(AppDbContext db, ICurrentUser user, IPermissionService permissions,
    IPrivateFileStorage storage, IDocumentService documents) : IFileService
{
    private Guid OrgId => user.OrganizationId ?? throw new ForbiddenException();
    private Guid UserId => user.UserId ?? throw new ForbiddenException();

    public async Task<Paged<FileDto>> ListAsync(FileListQuery query)
    {
        await EnsureActiveMemberAsync();
        if (query.Page < 1 || query.Page > 100000 || query.PageSize < 1 || query.PageSize > 100)
            throw new ValidationException("Paginación no válida.");
        if (query.Visibility.HasValue && !Enum.IsDefined(query.Visibility.Value))
            throw new ValidationException("Carpeta no válida.");
        if (query.ProjectId.HasValue) await permissions.EnsureProjectPermissionAsync(query.ProjectId.Value, Permission.Leer);
        var q = await ReadableFilesAsync();
        if (query.ProjectId.HasValue) q = q.Where(f => f.ProjectId == query.ProjectId);
        if (query.Visibility.HasValue) q = q.Where(f => f.Visibility == query.Visibility);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            if (search.Length > 255) throw new ValidationException("La búsqueda es demasiado larga.");
            // The opaque code is also searchable, without broadening the authorized scope.
            if (search.StartsWith("ARC-", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParseExact(search[4..], "N", out var fileId)) q = q.Where(f => f.Id == fileId);
            else q = q.Where(f => f.OriginalName.Contains(search) || (f.Description != null && f.Description.Contains(search)));
        }
        if (query.EntityType is not null || query.EntityId.HasValue)
        {
            if (string.IsNullOrEmpty(query.EntityType) || !query.EntityId.HasValue)
                throw new ValidationException("Indica el tipo y el identificador del vínculo.");
            await EnsureReadableContextAsync(query.EntityType, query.EntityId.Value);
            q = q.Where(f => f.Visibility == FileVisibility.Context && db.Attachments.Any(a => a.StoredFileId == f.Id
                && a.OrganizationId == OrgId && a.EntityType == query.EntityType && a.EntityId == query.EntityId));
        }
        var total = await q.CountAsync();
        var rows = await q.OrderByDescending(f => f.CreatedAt).ThenBy(f => f.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync();
        return new(rows.Select(Dto).ToList(), total, query.Page, query.PageSize);
    }

    public async Task<FileDto> UploadAsync(FileUploadRequest request, Stream content, CancellationToken cancellationToken = default)
    {
        await EnsureActiveMemberAsync();
        var name = ValidateName(request.Name);
        if (request.Description?.Length > 2000) throw new ValidationException("La descripción admite hasta 2000 caracteres.");
        if (!Enum.IsDefined(request.Visibility)) throw new ValidationException("Carpeta no válida.");
        Guid? projectId = request.ProjectId;
        if (request.Visibility == FileVisibility.Context)
        {
            if (request.EntityType is null || !request.EntityId.HasValue)
                throw new ValidationException("El archivo necesita un contexto autorizado.");
            projectId = await EnsureWritableContextAsync(request.EntityType, request.EntityId.Value);
            if (request.ProjectId.HasValue && request.ProjectId != projectId) throw new ValidationException("El vínculo pertenece a otro proyecto.");
        }
        else
        {
            if (request.EntityType is not null || request.EntityId.HasValue)
                throw new ValidationException("Usa el contexto del registro para adjuntar un archivo; la carpeta no amplía su audiencia.");
            if (request.Visibility == FileVisibility.Public && !projectId.HasValue)
                throw new ValidationException("Selecciona el proyecto de la carpeta pública.");
            if (projectId.HasValue)
            {
                await permissions.EnsureProjectPermissionAsync(projectId.Value, Permission.Leer);
                await permissions.EnsureProjectPermissionAsync(projectId.Value,
                    request.Visibility == FileVisibility.Public ? Permission.Crear : Permission.Leer);
            }
        }
        var file = new StoredFile { OrganizationId = OrgId, OriginalName = name, Visibility = request.Visibility,
            ProjectId = projectId, Description = request.Description?.Trim(), UploaderId = UserId, CreatedBy = UserId };
        var blob = await storage.SaveAsync(OrgId, file.Id, Path.GetExtension(name).ToLowerInvariant(), content, request.SizeBytes, cancellationToken);
        file.InternalName = blob.Key; file.ContentType = blob.ContentType; file.SizeBytes = blob.SizeBytes;
        db.StoredFiles.Add(file);
        if (request.Visibility == FileVisibility.Context)
            db.Attachments.Add(new Attachment { StoredFileId = file.Id, OrganizationId = OrgId,
                EntityType = request.EntityType!, EntityId = request.EntityId!.Value, LinkedBy = UserId,
                Description = file.Description, CreatedBy = UserId });
        db.AuditLogs.Add(new AuditLog { OrganizationId = OrgId, ActorId = UserId, EntityType = "StoredFile",
            EntityId = file.Id, Action = "FileUploaded", NewState = file.Visibility.ToString(), CreatedBy = UserId });
        try { await db.SaveChangesAsync(cancellationToken); }
        catch { await storage.RemoveAsync(blob.Key); throw; }
        return Dto(file);
    }

    public async Task<FileContent> OpenAsync(Guid id, bool preview = false, CancellationToken cancellationToken = default)
    {
        await EnsureActiveMemberAsync();
        // Use exactly the same predicate as list/count. A guessed code grants no access.
        var file = await (await ReadableFilesAsync()).FirstOrDefaultAsync(f => f.Id == id)
            ?? throw new NotFoundException("Archivo no disponible.");
        if (preview && file.ContentType is not ("image/png" or "image/jpeg" or "image/gif" or "image/webp"))
            throw new ValidationException("Este formato se ofrece únicamente como descarga.");
        return new(await storage.OpenAsync(file.InternalName, cancellationToken), file.OriginalName, file.ContentType);
    }

    public async Task DeleteAsync(Guid id)
    {
        await EnsureActiveMemberAsync();
        var file = await (await ReadableFilesAsync()).FirstOrDefaultAsync(f => f.Id == id)
            ?? throw new NotFoundException("Archivo no disponible.");
        if (file.UploaderId != UserId) throw new ForbiddenException("Solo quien subió el archivo puede retirarlo.");
        file = await db.StoredFiles.SingleAsync(f => f.Id == id);
        file.IsDeleted = true; file.DeletedAt = DateTime.UtcNow; file.UpdatedAt = DateTime.UtcNow; file.UpdatedBy = UserId;
        db.AuditLogs.Add(new AuditLog { OrganizationId = OrgId, ActorId = UserId, EntityType = "StoredFile",
            EntityId = id, Action = "FileRemoved", CreatedBy = UserId });
        await db.SaveChangesAsync(); // Retain the blob for recovery; do not delete shared evidence on disk.
    }

    private async Task<IQueryable<StoredFile>> ReadableFilesAsync()
    {
        var projectIds = new List<Guid>();
        foreach (var projectId in await db.Projects.AsNoTracking().Where(p => p.OrganizationId == OrgId).Select(p => p.Id).ToListAsync())
            if ((await permissions.GetProjectPermissionsAsync(projectId)).Contains(Permission.Leer)) projectIds.Add(projectId);
        // Existing attachments retain their contextual audience, including document-level restrictions.
        var documentIds = new List<Guid>();
        if (await db.StoredFiles.AnyAsync(f => f.OrganizationId == OrgId && f.Visibility == FileVisibility.Context
            && db.Attachments.Any(a => a.StoredFileId == f.Id && a.EntityType == "Document")))
            foreach (var projectId in await db.Projects.Where(p => projectIds.Contains(p.Id) && p.Status != ProjectStatus.Archivado).Select(p => p.Id).ToListAsync())
                documentIds.AddRange((await documents.ListAsync(projectId)).Select(d => d.Id));
        var isAdmin = await permissions.IsOrganizationAdminAsync();
        var workIds = db.WorkItems.Where(w => w.OrganizationId == OrgId &&
            (w.ProjectId.HasValue ? projectIds.Contains(w.ProjectId.Value) : w.AuthorId == UserId || isAdmin)).Select(w => w.Id);
        var ticketScope = await TicketAccessRules.LoadScopeAsync(db, user, permissions);
        var ticketIds = TicketAccessRules.ReadableQuery(db, ticketScope).Select(t => t.Id);
        var messageIds = db.Messages.Where(m => !m.IsDeleted && db.Channels.Any(c => c.Id == m.ChannelId
            && c.OrganizationId == OrgId && !c.IsArchived && (c.Type == ChannelType.Proyecto
                ? c.ProjectId.HasValue && projectIds.Contains(c.ProjectId.Value)
                : db.ChannelMembers.Any(cm => cm.ChannelId == c.Id && cm.UserId == UserId)))).Select(m => m.Id);
        return db.StoredFiles.AsNoTracking().Where(f => f.OrganizationId == OrgId &&
            ((f.Visibility == FileVisibility.Private && f.UploaderId == UserId
                && (!f.ProjectId.HasValue || projectIds.Contains(f.ProjectId.Value)))
            || (f.Visibility == FileVisibility.Public && f.ProjectId.HasValue && projectIds.Contains(f.ProjectId.Value))
            || (f.Visibility == FileVisibility.Context && db.Attachments.Any(a => a.StoredFileId == f.Id && a.OrganizationId == OrgId &&
                ((a.EntityType == "WorkItem" && workIds.Contains(a.EntityId))
                || (a.EntityType == "Document" && documentIds.Contains(a.EntityId))
                || (a.EntityType == "Project" && projectIds.Contains(a.EntityId))
                || (a.EntityType == "Ticket" && ticketIds.Contains(a.EntityId))
                || (a.EntityType == "Message" && messageIds.Contains(a.EntityId)))))
            // Legacy unattached blobs were never shared. Recover them only for the uploader.
            || (f.Visibility == FileVisibility.Context && f.UploaderId == UserId
                && !db.Attachments.Any(a => a.StoredFileId == f.Id))));
    }

    private async Task<Guid?> EnsureWritableContextAsync(string type, Guid id)
    {
        Guid? projectId;
        switch (type)
        {
            case "WorkItem":
                await permissions.EnsureCanReadWorkItemAsync(id);
                var work = await db.WorkItems.AsNoTracking().SingleAsync(w => w.Id == id && w.OrganizationId == OrgId);
                projectId = work.ProjectId;
                if (!projectId.HasValue && work.AuthorId != UserId) throw new ForbiddenException();
                break;
            case "Document":
                var document = await documents.GetAsync(id);
                if (!document.CanEdit) throw new ForbiddenException();
                projectId = document.ProjectId;
                break;
            case "Project": projectId = id; break;
            default: throw new ValidationException("Adjunta el archivo a una tarea, documento o proyecto compatible.");
        }
        if (projectId.HasValue)
        {
            await permissions.EnsureProjectPermissionAsync(projectId.Value, Permission.Leer);
            await permissions.EnsureProjectPermissionAsync(projectId.Value, Permission.Comentar);
        }
        return projectId;
    }

    private async Task EnsureReadableContextAsync(string type, Guid id)
    {
        switch (type)
        {
            case "WorkItem": await permissions.EnsureCanReadWorkItemAsync(id); return;
            case "Document": await documents.GetAsync(id); return;
            case "Project": await permissions.EnsureProjectPermissionAsync(id, Permission.Leer); return;
            case "Ticket":
                var ticket = await db.Tickets.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id && t.OrganizationId == OrgId)
                    ?? throw new NotFoundException("Contexto no disponible.");
                TicketAccessRules.RequireRead(ticket, await TicketAccessRules.LoadScopeAsync(db, user, permissions));
                return;
            case "Message":
                var message = await db.Messages.AsNoTracking().Include(m => m.Channel)
                    .FirstOrDefaultAsync(m => m.Id == id && !m.IsDeleted && m.Channel!.OrganizationId == OrgId && !m.Channel.IsArchived)
                    ?? throw new NotFoundException("Contexto no disponible.");
                if (message.Channel!.Type == ChannelType.Proyecto && message.Channel.ProjectId.HasValue)
                    await permissions.EnsureProjectPermissionAsync(message.Channel.ProjectId.Value, Permission.Leer);
                else if (!await db.ChannelMembers.AnyAsync(m => m.ChannelId == message.ChannelId && m.UserId == UserId)) throw new ForbiddenException();
                return;
            default: throw new ValidationException("Tipo de contexto no admitido.");
        }
    }

    private async Task EnsureActiveMemberAsync()
    {
        if (!await db.Users.AsNoTracking().AnyAsync(u => u.Id == UserId && u.IsActive)
            || !await db.OrganizationMembers.AsNoTracking().AnyAsync(m => m.OrganizationId == OrgId && m.UserId == UserId && m.IsActive))
            throw new ForbiddenException("La membresía ya no está activa.");
    }
    private FileDto Dto(StoredFile f) => new(f.Id, f.Id, $"ARC-{f.Id:N}", f.OriginalName, f.ContentType,
        f.SizeBytes, f.Description, f.ProjectId, f.Visibility.ToString(), f.UploaderId, f.CreatedAt, f.UploaderId == UserId);
    private static string ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name != Path.GetFileName(name)
            || name.Contains('\\') || name.Contains('/') || name.Any(char.IsControl)
            || name.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || name.EndsWith('.') || name.EndsWith(' '))
            throw new ValidationException("El nombre del archivo no es válido.");
        return name;
    }
}
