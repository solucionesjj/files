using Files.Application.Abstractions;
using Files.Application.Files;
using Files.Application.Storage;
using Microsoft.Extensions.Logging;

namespace Files.Api.Endpoints;

/// <summary>
/// Bearer capability URL that streams one file authorized by a 120 s single-use download grant
/// (ADR-0003/0005). No bearer header (the browser navigates here), <c>no-store</c>/<c>no-referrer</c>,
/// counted only when the stream completes, and never a presigned S3 URL.
/// </summary>
public static partial class DownloadGrantEndpoints
{
    public static void MapDownloadGrantEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/v1/download-grants/{grantToken}", StreamAsync);
    }

    private static async Task<IResult> StreamAsync(
        HttpContext context,
        string grantToken,
        IDownloadGrantService grants,
        IObjectStore objectStore,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Files.Api.DownloadGrant");

        var redeem = await grants.RedeemAsync(grantToken, ct);
        if (!redeem.IsSuccess)
        {
            return ToProblem(context, redeem.Error!);
        }

        var redeemed = redeem.Value!;
        var file = redeemed.File;

        Stream stream;
        try
        {
            stream = await objectStore.GetObjectAsync(file.StorageKey!, ct);
        }
        catch (ObjectMissingException)
        {
            await grants.FailAsync(redeemed.GrantId, file.Id, "object_missing", CancellationToken.None);
            return Problems.Response(context, StatusCodes.Status404NotFound, "file_unavailable", "File is not available");
        }

        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = file.ContentType;
        context.Response.ContentLength = file.SizeBytes;
        context.Response.Headers["Content-Disposition"] = ContentDisposition.Build(file.OriginalFilename);
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["Cache-Control"] = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";

        var completed = false;
        try
        {
            await CopyToAsync(stream, context.Response.Body, ct);
            completed = true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Client disconnected; an incomplete stream is never counted (ADR-0005).
        }
        finally
        {
            await stream.DisposeAsync();
        }

        if (completed)
        {
            try
            {
                await grants.CompleteAsync(redeemed.GrantId, file.Id, file.SizeBytes, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Bytes were delivered but the count/event write failed; accepted gap (ADR-0005).
                Log.CompletionFailed(logger, redeemed.GrantId, ex);
            }
        }
        else
        {
            await grants.FailAsync(redeemed.GrantId, file.Id, "client_disconnect", CancellationToken.None);
        }

        return Results.Empty;
    }

    private static async Task CopyToAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        int read;
        while ((read = await source.ReadAsync(buffer, ct)) > 0)
        {
            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }
    }

    private static IResult ToProblem(HttpContext context, ServiceError error) =>
        Problems.Response(context, error.Status, error.Code, error.Title, error.Detail);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Failed to record completed download for grant {GrantId}")]
        public static partial void CompletionFailed(ILogger logger, Guid grantId, Exception exception);
    }
}
