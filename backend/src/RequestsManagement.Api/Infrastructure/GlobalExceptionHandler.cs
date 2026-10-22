using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace RequestsManagement.Api.Infrastructure;

/// <summary>
/// Catches any unhandled exception that escapes the controller pipeline,
/// logs it with a correlation trace ID, and returns RFC 7807 ProblemDetails.
///
/// Rules:
/// - Never expose stack traces or internal messages outside Development.
/// - OperationCanceledException is not an error; log at Debug and return 499.
/// - All other exceptions are HTTP 500 with a safe, generic message.
/// </summary>
internal sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IHostEnvironment env)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext ctx,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException)
        {
            logger.LogDebug("Request {TraceId} cancelled", ctx.TraceIdentifier);
            // 499 Client Closed Request — not a standard HTTP status,
            // but conventional for cancelled requests; no body needed.
            ctx.Response.StatusCode = 499;
            return true;
        }

        logger.LogError(
            exception,
            "Unhandled exception on {Method} {Path} — TraceId: {TraceId}",
            ctx.Request.Method,
            ctx.Request.Path,
            ctx.TraceIdentifier);

        var detail = env.IsDevelopment()
            ? exception.Message
            : "An unexpected error occurred. Contact support with TraceId: " + ctx.TraceIdentifier;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "Internal Server Error",
            Detail = detail,
            Instance = ctx.Request.Path,
            Extensions = { ["traceId"] = ctx.TraceIdentifier }
        };

        ctx.Response.StatusCode = StatusCodes.Status500InternalServerError;
        ctx.Response.ContentType = "application/problem+json";

        await ctx.Response.WriteAsJsonAsync(problem, cancellationToken);
        return true;
    }
}
