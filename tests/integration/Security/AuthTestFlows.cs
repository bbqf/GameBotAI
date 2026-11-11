using GameBot.Service;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameBot.Service.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace GameBot.IntegrationTests.Security;

/// <summary>Shared steps for the sign-in tests (feature 187).</summary>
internal static class AuthTestFlows {
  public const string ProbePath = "/api/version";

  public static HttpClient NewClient(AuthTestHost host) =>
    host.CreateClient(new WebApplicationFactoryClientOptions { HandleCookies = false, AllowAutoRedirect = false });

  public static async Task<(HttpStatusCode Status, string Body)> GetAsync(
    HttpClient client, string path, string? remote = null, string? host = null, string? bearer = null, string? cookie = null) {
    using var request = AuthTestHost.Request(HttpMethod.Get, path, remote, host, bearer);
    if (cookie is not null) {
      request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    using var response = await client.SendAsync(request).ConfigureAwait(false);
    return (response.StatusCode, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
  }

  public static Task<HttpResponseMessage> PostAsync(
    HttpClient client, string path, object? body, string? remote = null, string? host = null, string? cookie = null, string? origin = null, Action<HttpRequestMessage>? configure = null) {
    var request = AuthTestHost.Request(HttpMethod.Post, path, remote, host);
    configure?.Invoke(request);
    if (cookie is not null) {
      request.Headers.TryAddWithoutValidation("Cookie", cookie);
    }

    if (origin is not null) {
      request.Headers.TryAddWithoutValidation("Origin", origin);
    }

    request.Content = body is null
      ? new StringContent(string.Empty, Encoding.UTF8, "application/json")
      : JsonContent.Create(body);
    return client.SendAsync(request);
  }

  /// <summary>Makes a code like the local command does, then registers a passkey with the fake verifier. Gives the cookie header value.</summary>
  public static async Task<string> RegisterAsync(
    AuthTestHost host, HttpClient client, string credentialId = "AQID", string? name = "My phone", string? remote = null) {
    var code = new RegistrationCodeStore(host.AuthDir, TimeProvider.System).NewCode();
    using var options = await PostAsync(client, ApiRoutes.AuthRegisterOptions, new { code }, remote).ConfigureAwait(false);
    options.EnsureSuccessStatusCode();
    var ceremonyId = (await ReadJsonAsync(options).ConfigureAwait(false)).GetProperty("ceremonyId").GetString();
    using var complete = await PostAsync(client, ApiRoutes.AuthRegisterComplete, new {
      ceremonyId,
      name,
      credential = new { id = credentialId, rawId = credentialId, type = "public-key", valid = true, response = new { } }
    }, remote).ConfigureAwait(false);
    if (complete.StatusCode != HttpStatusCode.NoContent) {
      throw new InvalidOperationException("Registration failed: " + complete.StatusCode);
    }

    return CookieOf(complete) ?? throw new InvalidOperationException("No cookie.");
  }

  /// <summary>Signs in with a registered passkey and the fake verifier. Gives the response.</summary>
  public static async Task<HttpResponseMessage> SignInAsync(
    HttpClient client, string credentialId = "AQID", bool valid = true, string? remote = null) {
    using var options = await PostAsync(client, ApiRoutes.AuthSignInOptions, null, remote).ConfigureAwait(false);
    options.EnsureSuccessStatusCode();
    var ceremonyId = (await ReadJsonAsync(options).ConfigureAwait(false)).GetProperty("ceremonyId").GetString();
    return await PostAsync(client, ApiRoutes.AuthSignInComplete, new {
      ceremonyId,
      credential = new { id = credentialId, rawId = credentialId, type = "public-key", valid, response = new { } }
    }, remote).ConfigureAwait(false);
  }

  public static async Task<JsonElement> ReadJsonAsync(HttpResponseMessage response) {
    var text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
    using var doc = JsonDocument.Parse(text);
    return doc.RootElement.Clone();
  }

  /// <summary>Gives <c>name=value</c> of the session cookie in a response, or null.</summary>
  public static string? CookieOf(HttpResponseMessage response) {
    if (!response.Headers.TryGetValues("Set-Cookie", out var values)) {
      return null;
    }

    var line = values.FirstOrDefault(v => v.StartsWith(SessionStore.CookieName + "=", StringComparison.Ordinal));
    return line?.Split(';')[0];
  }

  public static string? SetCookieLine(HttpResponseMessage response) =>
    response.Headers.TryGetValues("Set-Cookie", out var values)
      ? values.FirstOrDefault(v => v.StartsWith(SessionStore.CookieName + "=", StringComparison.Ordinal))
      : null;

  public static string[] RecordLines(AuthTestHost host) {
    var path = Path.Combine(host.AuthDir, "sign-in-records.jsonl");
    return File.Exists(path) ? File.ReadAllLines(path) : Array.Empty<string>();
  }
}
