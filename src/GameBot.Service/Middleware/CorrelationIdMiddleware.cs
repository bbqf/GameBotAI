using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Middleware;

internal sealed class CorrelationIdMiddleware : IMiddleware {
  public const string HeaderName = "X-Correlation-ID";
  private readonly ILogger<CorrelationIdMiddleware> _logger;

  public CorrelationIdMiddleware(ILogger<CorrelationIdMiddleware> logger) {
    _logger = logger;
  }

  private static string? SanitizeForLogging(string? value) {
    if (value is null) {
      return null;
    }

    // Remove newline characters to prevent log forging / line injection
    var sanitized = value.Replace("\r", string.Empty, StringComparison.Ordinal)
                         .Replace("\n", string.Empty, StringComparison.Ordinal);

    return sanitized;
  }

  public async Task InvokeAsync(HttpContext context, RequestDelegate next) {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);

    var rawCorrelationId = context.Request.Headers.TryGetValue(HeaderName, out var values) && values.Count > 0
        ? values[0]!.ToString()!
        : Guid.NewGuid().ToString("N");

    var correlationId = SanitizeForLogging(rawCorrelationId) ?? string.Empty;

    context.Response.Headers[HeaderName] = correlationId;

    var activity = Activity.Current;
    var scopeState = new Dictionary<string, object?> {
      ["CorrelationId"] = correlationId,
      ["TraceId"] = SanitizeForLogging(activity?.TraceId.ToString()),
      ["SpanId"] = SanitizeForLogging(activity?.SpanId.ToString()),
      ["RequestPath"] = SanitizeForLogging(context.Request.Path.Value),
      ["Method"] = SanitizeForLogging(context.Request.Method)
    };

    using (_logger.BeginScope(scopeState)) {
      await next(context).ConfigureAwait(false);
    }
  }
}

internal static class CorrelationIdMiddlewareExtensions {
  public static IApplicationBuilder UseCorrelationIds(this IApplicationBuilder app)
      => app.UseMiddleware<CorrelationIdMiddleware>();
}
