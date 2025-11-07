using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Middleware;

public sealed class CorrelationIdMiddleware : IMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly ILogger<CorrelationIdMiddleware> _logger;

    public CorrelationIdMiddleware(ILogger<CorrelationIdMiddleware> logger)
    {
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var values)
            && values.Count > 0
            ? values[0]!.ToString()
            : Guid.NewGuid().ToString("N");

        context.Response.Headers[HeaderName] = correlationId;

        var activity = Activity.Current;
        var scopeState = new Dictionary<string, object?>
        {
            ["CorrelationId"] = correlationId,
            ["TraceId"] = activity?.TraceId.ToString(),
            ["SpanId"] = activity?.SpanId.ToString(),
            ["RequestPath"] = context.Request.Path.Value,
            ["Method"] = context.Request.Method
        };

        using (_logger.BeginScope(scopeState))
        {
            await next(context).ConfigureAwait(false);
        }
    }
}

public static class CorrelationIdMiddlewareExtensions
{
    public static IApplicationBuilder UseCorrelationIds(this IApplicationBuilder app)
        => app.UseMiddleware<CorrelationIdMiddleware>();
}
