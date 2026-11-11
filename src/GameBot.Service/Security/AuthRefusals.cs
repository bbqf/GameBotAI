using System.Text;

namespace GameBot.Service.Security;

/// <summary>
/// The two fixed refusal bodies of the sign-in function (contract A-1 and A-2, FR-018).
/// The bytes of A-1 are the same for each cause, so a refusal shows nothing about passkeys or codes.
/// </summary>
internal static class AuthRefusals {
  private static readonly byte[] SignInNecessaryBody = Encoding.UTF8.GetBytes(
    "{\"error\":{\"code\":\"unauthorized\",\"message\":\"Sign-in is necessary.\",\"hint\":null}}");

  private static readonly byte[] RemoteRefusedBody = Encoding.UTF8.GetBytes(
    "{\"error\":{\"code\":\"remote_access_refused\",\"message\":\"The service refuses this request.\",\"hint\":null}}");

  /// <summary>Writes refusal A-1 (401).</summary>
  public static Task WriteSignInNecessaryAsync(HttpResponse response) =>
    WriteAsync(response, StatusCodes.Status401Unauthorized, SignInNecessaryBody);

  /// <summary>Writes refusal A-2 (403).</summary>
  public static Task WriteRemoteRefusedAsync(HttpResponse response) =>
    WriteAsync(response, StatusCodes.Status403Forbidden, RemoteRefusedBody);

  /// <summary>The result of refusal A-1, for endpoint handlers.</summary>
  public static IResult SignInNecessary() => Results.Content(Encoding.UTF8.GetString(SignInNecessaryBody), "application/json", Encoding.UTF8, StatusCodes.Status401Unauthorized);

  private static Task WriteAsync(HttpResponse response, int status, byte[] body) {
    response.StatusCode = status;
    response.ContentType = "application/json";
    response.ContentLength = body.Length;
    return response.Body.WriteAsync(body, 0, body.Length);
  }
}

