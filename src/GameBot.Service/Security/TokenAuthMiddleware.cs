using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace GameBot.Service.Security;

internal static class TokenAuthMiddleware {
  public static async Task Invoke(HttpContext context, RequestDelegate next, string token) {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);
    if (TryGetBearer(context, out var provided) && Matches(provided, token)) {
      await next(context).ConfigureAwait(false);
      return;
    }

    context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
    await context.Response.WriteAsJsonAsync(new {
      error = new { code = "unauthorized", message = "Invalid or missing bearer token.", hint = "Provide Authorization: Bearer <token>" }
    }).ConfigureAwait(false);
  }

  /// <summary>Reads the bearer token of the request. Gives false when the request has none.</summary>
  public static bool TryGetBearer(HttpContext context, out string provided) {
    ArgumentNullException.ThrowIfNull(context);
    var auth = context.Request.Headers.Authorization.ToString();
    if (auth.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) {
      provided = auth.Substring("Bearer ".Length).Trim();
      return true;
    }

    provided = string.Empty;
    return false;
  }

  /// <summary>Compares two tokens in constant time.</summary>
  public static bool Matches(string provided, string token) =>
    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(token));
}
