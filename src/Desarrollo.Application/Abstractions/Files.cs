using Desarrollo.Application.Contracts;
using Desarrollo.Domain.Files;

namespace Desarrollo.Application.Abstractions;

public record FileListQuery(Guid? ProjectId = null, FileVisibility? Visibility = null,
    string? Search = null, int Page = 1, int PageSize = 50, string? EntityType = null, Guid? EntityId = null);
public record FileUploadRequest(string Name, long SizeBytes, FileVisibility Visibility = FileVisibility.Private,
    Guid? ProjectId = null, string? Description = null, string? EntityType = null, Guid? EntityId = null);
public record StoredBlob(string Key, long SizeBytes, string ContentType);
public record FileContent(Stream Stream, string Name, string ContentType);

public interface IFileService
{
    Task<Paged<FileDto>> ListAsync(FileListQuery query);
    Task<FileDto> UploadAsync(FileUploadRequest request, Stream content, CancellationToken cancellationToken = default);
    Task<FileContent> OpenAsync(Guid id, bool preview = false, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id);
}

public interface IPrivateFileStorage
{
    Task<StoredBlob> SaveAsync(Guid organizationId, Guid fileId, string extension, Stream content,
        long expectedSize, CancellationToken cancellationToken = default);
    Task<Stream> OpenAsync(string key, CancellationToken cancellationToken = default);
    Task RemoveAsync(string key);
}
