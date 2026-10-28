using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;
using Desarrollo.Domain.Files;
using Desarrollo.Infrastructure.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Desarrollo.Web.Controllers;

[ApiController, Route("api/files"), Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class FilesController(IFileService files) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List([FromQuery] Guid? projectId, [FromQuery] string? visibility,
        [FromQuery] string? search, [FromQuery] int page = 1, [FromQuery] int pageSize = 50,
        [FromQuery] string? entityType = null, [FromQuery] Guid? entityId = null)
        => Ok(await files.ListAsync(new(projectId, ParseVisibility(visibility), search, page, pageSize, entityType, entityId)));

    [HttpPost, ValidateAntiForgeryToken]
    [RequestSizeLimit(LocalPrivateFileStorage.MaxBytes + 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = LocalPrivateFileStorage.MaxBytes + 1024 * 1024)]
    public async Task<IActionResult> Upload([FromForm] UploadRequest req, CancellationToken cancellationToken)
    {
        var file = req.File ?? throw new ValidationException("Archivo requerido.");
        await using var content = file.OpenReadStream();
        return Ok(await files.UploadAsync(new(file.FileName, file.Length,
            ParseVisibility(req.Visibility) ?? (req.EntityId.HasValue ? FileVisibility.Context : FileVisibility.Private),
            req.ProjectId, req.Description, req.EntityType, req.EntityId), content, cancellationToken));
    }
    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id, CancellationToken cancellationToken)
    {
        var file = await files.OpenAsync(id, false, cancellationToken);
        SetContentHeaders();
        return File(file.Stream, file.ContentType, file.Name, enableRangeProcessing: true);
    }
    [HttpGet("{id:guid}/preview")]
    public async Task<IActionResult> Preview(Guid id, CancellationToken cancellationToken)
    {
        var file = await files.OpenAsync(id, true, cancellationToken);
        SetContentHeaders();
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
        return File(file.Stream, file.ContentType, enableRangeProcessing: true);
    }
    [HttpDelete("{id:guid}"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(Guid id) { await files.DeleteAsync(id); return NoContent(); }
    private void SetContentHeaders()
    {
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["Cross-Origin-Resource-Policy"] = "same-origin";
    }
    private static FileVisibility? ParseVisibility(string? value)
    {
        if (value is null) return null;
        if (!Enum.TryParse<FileVisibility>(value, true, out var parsed) || !Enum.IsDefined(parsed))
            throw new ValidationException("Carpeta no válida.");
        return parsed;
    }
    public record UploadRequest(IFormFile? File, string? EntityType, Guid? EntityId, string? Description,
        Guid? ProjectId = null, string? Visibility = null);
}
