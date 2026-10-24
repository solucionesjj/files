using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Files.Api.Middleware;

/// <summary>
/// Converts unhandled exceptions into an RFC 9457 <see cref="ProblemDetails"/> response. Never
/// leaks stack traces, SQL, keys, tokens, or the raw request path (paths may carry link/grant
/// tokens), and never logs the request body.
/// </summary>
public sealed partial class ExceptionHandlingMiddleware(
    RequestDelegate next,
    ILogger<ExceptionHandlingMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception exception)
        {
            // Route templates are logged, never the raw path.
            var route = context.GetEndpoint()?.DisplayName ?? "<unmatched>";
            Log.UnhandledException(logger, exception, context.Request.Method, route);

            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;

            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occurred.",
                Detail = "An unexpected error occurred.",
                Type = "https://www.rfc-editor.org/rfc/rfc9457.html",
            };
            problem.Extensions["code"] = "internal_error";
            problem.Extensions["traceId"] = CorrelationIdMiddleware.GetCorrelationId(context);

            await context.Response.WriteAsJsonAsync(problem, context.RequestAborted);
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Unhandled exception while handling {Method} {Route}")]
        public static partial void UnhandledException(ILogger logger, Exception exception, string method, string route);
    }
}
