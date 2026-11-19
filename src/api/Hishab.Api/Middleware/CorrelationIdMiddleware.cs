using System.Text.RegularExpressions;
using Serilog.Context;

namespace Hishab.Api.Middleware;

/// <summary>
/// Gives every request one correlation ID: reuses a well-formed incoming
/// <c>X-Correlation-ID</c> (so a caller can trace across services) or creates one, echoes it
/// in the response, sets <see cref="HttpContext.TraceIdentifier"/>, and adds it to every log line.
/// </summary>
public sealed partial class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";
    public const string LogProperty = "CorrelationId";

    // Short and boring on purpose: a caller must not be able to inject newlines or huge values into logs.
    [GeneratedRegex("^[A-Za-z0-9._:-]{1,64}$", RegexOptions.CultureInvariant)]
    private static partial Regex SafeId();

    public async Task InvokeAsync(HttpContext context)
    {
        var incoming = context.Request.Headers[HeaderName].ToString();
        var correlationId = SafeId().IsMatch(incoming) ? incoming : Guid.NewGuid().ToString("N");

        context.TraceIdentifier = correlationId;
        context.Response.OnStarting(() =>
        {
            context.Response.Headers[HeaderName] = correlationId;
            return Task.CompletedTask;
        });

        using (LogContext.PushProperty(LogProperty, correlationId))
        {
            await next(context);
        }
    }
}
