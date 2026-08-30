using Serilog.Context;

namespace AiContentFactory.Api.Middleware;

/// <summary>
/// Ensures every request carries an X-Correlation-Id (generating one if the
/// caller didn't supply it) and pushes it into the Serilog LogContext so it
/// shows up on every log line for that request - essential once background
/// jobs (Hangfire) start logging under the same correlation id.
/// </summary>
public class CorrelationIdMiddleware
{
    private const string HeaderName = "X-Correlation-Id";
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var existing) && !string.IsNullOrWhiteSpace(existing)
            ? existing.ToString()
            : Guid.NewGuid().ToString();

        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
