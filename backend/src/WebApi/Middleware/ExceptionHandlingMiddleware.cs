using System.Net;
using System.Text.Json;
using DocuArchive.Application;

namespace DocuArchive.WebApi.Middleware;

/// <summary>
/// Global exception handler. Returns a generic problem response and never leaks stack
/// traces, exception messages from internals, or secrets to the client. Details go to the log only.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (AppException ex)
        {
            _logger.LogWarning(ex, "Application error {StatusCode} on {Path}", ex.StatusCode, context.Request.Path);
            await WriteProblemAsync(context, ex.StatusCode, ex.Message, ex.Code);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error on {Path}", context.Request.Path);
            await WriteProblemAsync(context, (int)HttpStatusCode.InternalServerError, "An unexpected error occurred.", null);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, int statusCode, string message, string? code)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "application/problem+json";
        var problem = new
        {
            type = "https://docuarchive.local/problems/error",
            title = statusCode switch
            {
                400 => "Bad Request",
                401 => "Unauthorized",
                403 => "Forbidden",
                404 => "Not Found",
                409 => "Conflict",
                413 => "Payload Too Large",
                423 => "Locked",
                _ => "Error"
            },
            status = statusCode,
            detail = message,
            code
        };
        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
    }
}
