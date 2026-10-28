using Desarrollo.Application.Common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Desarrollo.Web.Middleware;

public class ApiExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ApiExceptionMiddleware> _logger;

    public ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            context.Response.StatusCode = ex.StatusCode;
            var problem = new ProblemDetails
            {
                Title = ex.Message,
                Status = ex.StatusCode,
            }.WithCode(ex.Code);
            if (ex is ConcurrencyConflictException conflict && conflict.Current is not null) problem.Extensions["current"] = conflict.Current;
            await context.Response.WriteAsJsonAsync(problem);
        }
        catch (DbUpdateConcurrencyException)
        {
            context.Response.StatusCode = 409;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Otra persona modificó el registro. Conserva tu edición y revisa la versión actual.", Status = 409,
            }.WithCode("CONCURRENCY"));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error no controlado");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = "Error interno del servidor.",
                Status = 500,
            }.WithCode("INTERNAL"));
        }
    }
}

internal static class ProblemDetailsExtensions
{
    public static ProblemDetails WithCode(this ProblemDetails p, string code)
    {
        p.Extensions["code"] = code;
        return p;
    }
}
