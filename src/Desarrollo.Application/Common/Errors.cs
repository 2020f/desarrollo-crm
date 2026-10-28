namespace Desarrollo.Application.Common;

public static class Errors
{
    public static readonly AppError Unauthorized = new("AUTH", "No autenticado.");
    public static AppError Forbidden(string? detail = null) => new("FORBIDDEN", detail ?? "No tiene permisos para esta operación.");
    public static AppError NotFound(string entity, object? id = null) => new("NOT_FOUND", $"{entity} no encontrado" + (id is null ? "." : $" ({id})."));
    public static AppError Validation(string detail) => new("VALIDATION", detail);
    public static AppError Conflict(string detail) => new("CONFLICT", detail);
    public static AppError InvalidState(string detail) => new("INVALID_STATE", detail);
}

public readonly record struct AppError(string Code, string Message);

public readonly record struct AppResult<T>
{
    public T? Value { get; init; }
    public AppError? Error { get; init; }
    public bool IsSuccess => Error is null;
    public static AppResult<T> Ok(T value) => new() { Value = value };
    public static AppResult<T> Fail(AppError error) => new() { Error = error };
}

public class AppException : Exception
{
    public string Code { get; }
    public int StatusCode { get; }
    public AppException(string code, string message, int statusCode = 400) : base(message)
    {
        Code = code;
        StatusCode = statusCode;
    }
}

public class ForbiddenException : AppException
{
    public ForbiddenException(string? message = null) : base("FORBIDDEN", message ?? "No tiene permisos para esta operación.", 403) { }
}

public class NotFoundException : AppException
{
    public NotFoundException(string message) : base("NOT_FOUND", message, 404) { }
}

public class ValidationException : AppException
{
    public ValidationException(string message) : base("VALIDATION", message, 400) { }
}

public class InvalidStateException : AppException
{
    public InvalidStateException(string message) : base("INVALID_STATE", message, 409) { }
}

public class ConcurrencyConflictException : AppException
{
    public object? Current { get; }
    public ConcurrencyConflictException(string message = "Otra persona modificó este registro. Conserva tu edición y revisa la versión actual.", object? current = null)
        : base("CONCURRENCY", message, 409) => Current = current;
}
