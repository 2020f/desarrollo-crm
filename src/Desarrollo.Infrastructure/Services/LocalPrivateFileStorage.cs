using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Desarrollo.Application.Abstractions;
using Desarrollo.Application.Common;

namespace Desarrollo.Infrastructure.Services;

/// <summary>Private durable disk adapter. Only generated keys are accepted, never HTTP paths.</summary>
public sealed partial class LocalPrivateFileStorage : IPrivateFileStorage
{
    public const long MaxBytes = 50L * 1024 * 1024;
    private readonly string _root;
    private static readonly Dictionary<string, string> MimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png", [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".pdf"] = "application/pdf",
        [".txt"] = "text/plain", [".md"] = "text/markdown", [".csv"] = "text/csv", [".zip"] = "application/zip",
        [".doc"] = "application/msword", [".xls"] = "application/vnd.ms-excel", [".ppt"] = "application/vnd.ms-powerpoint",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };
    public LocalPrivateFileStorage(string root) => _root = Path.GetFullPath(root);

    public async Task<StoredBlob> SaveAsync(Guid organizationId, Guid fileId, string extension, Stream content,
        long expectedSize, CancellationToken cancellationToken = default)
    {
        if (!MimeTypes.TryGetValue(extension, out var mime)) throw new ValidationException("Tipo de archivo no permitido.");
        if (expectedSize <= 0 || expectedSize > MaxBytes) throw new ValidationException("El archivo debe tener entre 1 byte y 50 MB.");
        var staging = Path.Combine(_root, ".staging");
        Directory.CreateDirectory(staging);
        var temporary = Path.Combine(staging, $"{Guid.NewGuid():N}.tmp");
        var key = $"{organizationId:N}/{fileId:N}{extension.ToLowerInvariant()}";
        var destination = ResolveKey(key);
        try
        {
            await using (var target = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long length = 0;
                int read;
                while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    length += read;
                    if (length > MaxBytes || length > expectedSize) throw new ValidationException("El tamaño recibido no coincide o supera 50 MB.");
                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (length != expectedSize) throw new ValidationException("El archivo llegó incompleto.");
                await target.FlushAsync(cancellationToken);
                target.Position = 0;
                await ValidateContentAsync(target, extension.ToLowerInvariant(), cancellationToken);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Move(temporary, destination, false);
            return new(key, expectedSize, mime);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    public Task<Stream> OpenAsync(string key, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var path = ResolveKey(key);
        if (!File.Exists(path)) throw new NotFoundException("Contenido no disponible.");
        return Task.FromResult<Stream>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true));
    }
    public Task RemoveAsync(string key)
    {
        var path = ResolveKey(key);
        if (File.Exists(path)) File.Delete(path);
        return Task.CompletedTask;
    }
    private string ResolveKey(string key)
    {
        if (!KeyPattern().IsMatch(key)) throw new ValidationException("Identificador de almacenamiento no válido.");
        var path = Path.GetFullPath(Path.Combine(_root, key.Replace('/', Path.DirectorySeparatorChar)));
        if (!path.StartsWith(_root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            throw new ValidationException("Ruta de almacenamiento no válida.");
        for (var part = path; part is not null && !StringComparer.Ordinal.Equals(part, _root); part = Path.GetDirectoryName(part))
            if ((File.Exists(part) || Directory.Exists(part)) && File.GetAttributes(part).HasFlag(FileAttributes.ReparsePoint))
                throw new ValidationException("Ruta de almacenamiento no válida.");
        return path;
    }
    [GeneratedRegex(@"^(?:[a-fA-F0-9]{32}/)?[a-fA-F0-9]{32}\.[a-zA-Z0-9]{1,10}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeyPattern();

    private static async Task ValidateContentAsync(FileStream file, string extension, CancellationToken token)
    {
        var header = new byte[32];
        var length = await file.ReadAsync(header, token);
        file.Position = 0;
        bool Starts(params byte[] bytes) => length >= bytes.Length && header.AsSpan(0, bytes.Length).SequenceEqual(bytes);
        var valid = extension switch
        {
            ".png" => length >= 24 && Starts(137, 80, 78, 71, 13, 10, 26, 10) && header.AsSpan(12, 4).SequenceEqual("IHDR"u8),
            ".jpg" or ".jpeg" => Starts(255, 216, 255),
            ".gif" => Starts(71, 73, 70, 56, 55, 97) || Starts(71, 73, 70, 56, 57, 97),
            ".webp" => length >= 12 && header.AsSpan(0, 4).SequenceEqual("RIFF"u8) && header.AsSpan(8, 4).SequenceEqual("WEBP"u8),
            ".pdf" => Starts(37, 80, 68, 70, 45),
            ".doc" or ".xls" or ".ppt" => Starts(208, 207, 17, 224, 161, 177, 26, 225),
            ".zip" or ".docx" or ".xlsx" or ".pptx" => Starts(80, 75, 3, 4) || Starts(80, 75, 5, 6),
            ".txt" or ".md" or ".csv" => true,
            _ => false,
        };
        if (!valid) throw new ValidationException("El contenido no coincide con la extensión del archivo.");
        if (extension is ".zip" or ".docx" or ".xlsx" or ".pptx")
        {
            try
            {
                using var archive = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
                if (archive.Entries.Count > 10000 || archive.Entries.Sum(e => e.Length) > 500L * 1024 * 1024)
                    throw new ValidationException("El archivo comprimido supera los límites admitidos.");
                if (extension != ".zip")
                {
                    var required = extension switch { ".docx" => "word/document.xml", ".xlsx" => "xl/workbook.xml", _ => "ppt/presentation.xml" };
                    if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry(required) is null
                        || archive.Entries.Any(e => e.Name.Equals("vbaProject.bin", StringComparison.OrdinalIgnoreCase)))
                        throw new ValidationException("El documento de Office no es válido o contiene macros no admitidas.");
                }
            }
            catch (InvalidDataException) { throw new ValidationException("El archivo comprimido está dañado."); }
        }
        if (extension is ".txt" or ".md" or ".csv")
        {
            try
            {
                using var reader = new StreamReader(file, new UTF8Encoding(false, true), detectEncodingFromByteOrderMarks: false, leaveOpen: true);
                var buffer = new char[8192];
                int read;
                while ((read = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
                    if (buffer.AsSpan(0, read).Contains('\0')) throw new ValidationException("El archivo de texto contiene datos binarios.");
            }
            catch (DecoderFallbackException) { throw new ValidationException("Los archivos de texto deben estar codificados en UTF-8."); }
        }
    }
}
