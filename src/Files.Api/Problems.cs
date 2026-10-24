using Files.Api.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace Files.Api;

/// <summary>
/// Builds RFC 9457 <c>application/problem+json</c> responses with the stable <c>code</c> extension.
/// Endpoints and the authentication challenge/forbid handlers all go through here so the shape is
/// consistent (see error-catalog.md and ADR-0012).
/// </summary>
public static class Problems
{
    public static IResult Response(HttpContext context, int status, string code, string title, string? detail = null) =>
        Results.Json(Create(context, status, code, title, detail), statusCode: status);

    public static Task WriteAsync(HttpContext context, int status, string code, string title, string? detail = null)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(Create(context, status, code, title, detail), context.RequestAborted);
    }

    public static ProblemDetails Create(HttpContext? context, int status, string code, string title, string? detail = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail ?? title,
            Type = $"https://files.sacservices.cloud/problems/{code}",
        };
        problem.Extensions["code"] = code;
        problem.Extensions["traceId"] = CorrelationIdMiddleware.GetCorrelationId(context);
        problem.Extensions["retryable"] = status is StatusCodes.Status429TooManyRequests or StatusCodes.Status502BadGateway or StatusCodes.Status503ServiceUnavailable;
        return problem;
    }
}
