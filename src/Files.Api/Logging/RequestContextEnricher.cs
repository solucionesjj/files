using System.Diagnostics;
using Files.Api.Middleware;
using Serilog.Core;
using Serilog.Events;

namespace Files.Api.Logging;

/// <summary>
/// Adds <c>TraceId</c>, <c>SpanId</c>, and <c>CorrelationId</c> to every log event so structured
/// logs can be joined to traces and to the <c>X-Correlation-Id</c> reported to clients.
/// </summary>
public sealed class RequestContextEnricher(IHttpContextAccessor httpContextAccessor) : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        var activity = Activity.Current;
        if (activity is not null)
        {
            logEvent.AddPropertyIfAbsent(new LogEventProperty("TraceId", new ScalarValue(activity.TraceId.ToString())));
            logEvent.AddPropertyIfAbsent(new LogEventProperty("SpanId", new ScalarValue(activity.SpanId.ToString())));
        }

        var correlationId = CorrelationIdMiddleware.GetCorrelationId(httpContextAccessor.HttpContext);
        if (!string.IsNullOrWhiteSpace(correlationId))
        {
            logEvent.AddPropertyIfAbsent(new LogEventProperty("CorrelationId", new ScalarValue(correlationId)));
        }
    }
}
