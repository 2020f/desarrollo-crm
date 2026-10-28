using System.Text.Json;
using Desarrollo.Application.Common;

namespace Desarrollo.Infrastructure.Services;

internal static class DocumentContentRules
{
    public const int MaxContentLength = 1_000_000;
    private static readonly HashSet<string> BlockNodes = new(StringComparer.Ordinal)
        { "paragraph", "heading", "bulletList", "orderedList", "taskList", "blockquote", "codeBlock", "horizontalRule" };
    private static readonly HashSet<string> MarkTypes = new(StringComparer.Ordinal) { "bold", "italic", "strike", "code", "link" };

    public static (string Content, string Format) Validate(string? content, string? format)
    {
        format = format?.Trim().ToLowerInvariant() ?? "plain";
        if (format is not ("plain" or "tiptap")) throw new ValidationException("El formato del documento no está permitido.");
        if (content is null || content.Length > MaxContentLength)
            throw new ValidationException($"El documento admite hasta {MaxContentLength} caracteres.");
        if (format == "plain") return (content, format);
        try
        {
            using var parsed = JsonDocument.Parse(content, new JsonDocumentOptions { MaxDepth = 64 });
            var budget = 10000;
            ValidateNode(parsed.RootElement, null, 0, ref budget);
            return (JsonSerializer.Serialize(parsed.RootElement), format);
        }
        catch (JsonException) { throw new ValidationException("El contenido enriquecido debe ser un documento JSON válido."); }
    }

    private static void ValidateNode(JsonElement node, string? parent, int depth, ref int budget)
    {
        if (depth > 30 || --budget < 0) throw new ValidationException("El documento contiene demasiados bloques o niveles.");
        Object(node, "type", "attrs", "content", "marks", "text");
        var type = RequiredString(node, "type", 32);
        if ((parent is null && type != "doc") || (parent is not null && !AllowedChild(parent, type)))
            throw new ValidationException("La estructura del documento contiene un bloque no permitido.");
        if (node.TryGetProperty("attrs", out var attrs)) ValidateAttributes(type, attrs);
        if (type == "text")
        {
            var text = RequiredString(node, "text", MaxContentLength);
            if (text.Length == 0) throw new ValidationException("Un nodo de texto no puede estar vacío.");
        }
        else if (node.TryGetProperty("text", out _)) throw new ValidationException("Texto fuera de un nodo de texto.");
        if (node.TryGetProperty("marks", out var marks))
        {
            if (type is not ("text" or "hardBreak") || parent == "codeBlock" || marks.ValueKind != JsonValueKind.Array || marks.GetArrayLength() > 8)
                throw new ValidationException("Las marcas de formato no son válidas.");
            foreach (var mark in marks.EnumerateArray()) ValidateMark(mark);
        }
        if (!node.TryGetProperty("content", out var children))
        {
            if (type is "bulletList" or "orderedList" or "taskList" or "listItem" or "taskItem" or "blockquote")
                throw new ValidationException("La lista o el bloque debe contener elementos.");
            return;
        }
        if (type is "text" or "hardBreak" or "horizontalRule" || children.ValueKind != JsonValueKind.Array)
            throw new ValidationException("El contenido del bloque no es válido.");
        if ((type is "bulletList" or "orderedList" or "taskList" or "listItem" or "taskItem" or "blockquote") && children.GetArrayLength() == 0)
            throw new ValidationException("La lista o el bloque debe contener elementos.");
        if (type is "listItem" or "taskItem")
        {
            if (children[0].ValueKind != JsonValueKind.Object || !children[0].TryGetProperty("type", out var first)
                || first.ValueKind != JsonValueKind.String || first.GetString() != "paragraph")
                throw new ValidationException("Cada elemento de lista debe comenzar con un párrafo.");
        }
        foreach (var child in children.EnumerateArray()) ValidateNode(child, type, depth + 1, ref budget);
    }

    private static bool AllowedChild(string parent, string type) => parent switch
    {
        "doc" or "blockquote" or "listItem" or "taskItem" => BlockNodes.Contains(type),
        "paragraph" or "heading" => type is "text" or "hardBreak",
        "codeBlock" => type == "text",
        "bulletList" or "orderedList" => type == "listItem",
        "taskList" => type == "taskItem",
        _ => false,
    };

    private static void ValidateAttributes(string type, JsonElement attrs)
    {
        if (attrs.ValueKind == JsonValueKind.Null) return;
        switch (type)
        {
            case "paragraph":
                Object(attrs, "textAlign"); Alignment(attrs); break;
            case "heading":
                Object(attrs, "level", "textAlign"); Alignment(attrs);
                if (attrs.TryGetProperty("level", out var level) && (level.ValueKind != JsonValueKind.Number
                    || !level.TryGetInt32(out var levelNumber) || levelNumber is < 1 or > 6))
                    throw new ValidationException("El nivel de encabezado no es válido.");
                break;
            case "orderedList":
                Object(attrs, "start", "type");
                if (attrs.TryGetProperty("start", out var start) && (start.ValueKind != JsonValueKind.Number
                    || !start.TryGetInt32(out var listStart) || listStart is < 1 or > 1000000))
                    throw new ValidationException("El inicio de la lista no es válido.");
                if (attrs.TryGetProperty("type", out var listType) && listType.ValueKind != JsonValueKind.Null
                    && (listType.ValueKind != JsonValueKind.String || listType.GetString() is not ("1" or "a" or "A" or "i" or "I")))
                    throw new ValidationException("El tipo de numeración no es válido.");
                break;
            case "taskItem":
                Object(attrs, "checked");
                if (attrs.TryGetProperty("checked", out var check) && check.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    throw new ValidationException("El estado de la casilla no es válido.");
                break;
            case "codeBlock":
                Object(attrs, "language");
                if (attrs.TryGetProperty("language", out var language) && language.ValueKind != JsonValueKind.Null
                    && (language.ValueKind != JsonValueKind.String || language.GetString()!.Length > 64
                        || language.GetString()!.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_' or '+' or '#' or '.'))))
                    throw new ValidationException("El lenguaje del bloque de código no es válido.");
                break;
            default: Object(attrs); break;
        }
    }

    private static void ValidateMark(JsonElement mark)
    {
        Object(mark, "type", "attrs");
        var type = RequiredString(mark, "type", 32);
        if (!MarkTypes.Contains(type)) throw new ValidationException("La marca de formato no está permitida.");
        if (!mark.TryGetProperty("attrs", out var attrs) || attrs.ValueKind == JsonValueKind.Null)
        {
            if (type == "link") throw new ValidationException("El enlace necesita una dirección.");
            return;
        }
        if (type != "link") { Object(attrs); return; }
        Object(attrs, "href", "target", "rel", "title", "class");
        var href = RequiredString(attrs, "href", 2048);
        if (href.Any(char.IsControl) || href.Any(char.IsWhiteSpace) || !Uri.TryCreate(href, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https" or "mailto")
            || ((uri.Scheme is "http" or "https") && string.IsNullOrWhiteSpace(uri.Host)))
            throw new ValidationException("Solo se permiten enlaces http, https o mailto válidos.");
        if (attrs.TryGetProperty("target", out var target) && target.ValueKind != JsonValueKind.Null
            && (target.ValueKind != JsonValueKind.String || target.GetString() is not ("_blank" or "_self")))
            throw new ValidationException("El destino del enlace no es válido.");
        if (attrs.TryGetProperty("class", out var cssClass) && cssClass.ValueKind != JsonValueKind.Null)
            throw new ValidationException("No se permiten clases personalizadas en los enlaces.");
        if (attrs.TryGetProperty("title", out var title) && title.ValueKind != JsonValueKind.Null
            && (title.ValueKind != JsonValueKind.String || title.GetString()!.Length > 250))
            throw new ValidationException("El título del enlace no es válido.");
        if (attrs.TryGetProperty("rel", out var rel) && rel.ValueKind != JsonValueKind.Null
            && (rel.ValueKind != JsonValueKind.String || rel.GetString()!.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Any(value => value is not ("noopener" or "noreferrer" or "nofollow"))))
            throw new ValidationException("Los atributos del enlace no son válidos.");
    }

    private static void Alignment(JsonElement attrs)
    {
        if (attrs.TryGetProperty("textAlign", out var value) && value.ValueKind != JsonValueKind.Null
            && (value.ValueKind != JsonValueKind.String || value.GetString() is not ("left" or "center" or "right" or "justify")))
            throw new ValidationException("La alineación del texto no es válida.");
    }

    private static void Object(JsonElement value, params string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new ValidationException("La estructura del contenido no es válida.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
            if (!names.Add(property.Name) || !allowed.Contains(property.Name, StringComparer.Ordinal))
                throw new ValidationException("El contenido incluye un atributo no permitido.");
    }

    private static string RequiredString(JsonElement node, string name, int limit)
    {
        if (!node.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String || value.GetString()!.Length > limit)
            throw new ValidationException("El documento contiene un valor de texto no válido.");
        return value.GetString()!;
    }
}
