using System.Net;

namespace GameBot.Service.Security;

internal static class TokenAuthMiddleware {
  public static async Task Invoke(HttpContext context, RequestDelegate next, string token) {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);
    var auth = context.Request.Headers.Authorization.ToString();
    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
      var provided = auth.Substring("Bearer ".Length).Trim();
      if (string.Equals(provided, token, StringComparison.Ordinal)) {
        await next(context).ConfigureAwait(false);
        return;
      }
    }

    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
    await context.Response.WriteAsJsonAsync(new {
      error = new { code = "unauthorized", message = "Invalid or missing bearer token.", hint = "Provide Authorization: Bearer <token>" }
    }).ConfigureAwait(false);
  }
}
