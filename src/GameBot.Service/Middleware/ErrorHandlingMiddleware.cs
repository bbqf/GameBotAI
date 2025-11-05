using System.Net;
using System.Text.Json;

namespace GameBot.Service.Middleware;

internal sealed class ErrorHandlingMiddleware : IMiddleware
{
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(ILogger<ErrorHandlingMiddleware> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);
        try
        {
            await next(context).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            context.Response.StatusCode = (int)HttpStatusCode.RequestTimeout;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = new { code = "request_timeout", message = "The request was canceled.", hint = (string?)null }
            })).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Intentional catch-all to produce a uniform error envelope
        catch (Exception ex)
        {
            Log.Unhandled(_logger, ex);
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(new
            {
                error = new { code = "internal_error", message = "An unexpected error occurred.", hint = ex.Message }
            })).ConfigureAwait(false);
        }
#pragma warning restore CA1031
    }
}

internal static class Log
{
    private static readonly Action<ILogger, string, Exception?> _unhandled =
        LoggerMessage.Define<string>(LogLevel.Error, new EventId(100, nameof(Unhandled)),
            "Unhandled exception: {Message}");

    public static void Unhandled(ILogger l, Exception ex) => _unhandled(l, ex.Message, ex);
}
