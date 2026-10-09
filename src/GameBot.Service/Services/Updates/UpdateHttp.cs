using GameBot.Domain.Updates;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Safe download rules (research R-004). The bot follows only https URLs on github.com and
/// *.githubusercontent.com. The bot follows each redirect step by hand and checks each host.
/// </summary>
internal static class UpdateHttp {
  public const int MaxRedirects = 5;
  public const string UserAgent = "GameBot-Updater";

  public static bool IsAllowedUrl(Uri? url) {
    if (url is null || !url.IsAbsoluteUri || url.Scheme != Uri.UriSchemeHttps) {
      return false;
    }

    var host = url.Host;
    return host.Equals("github.com", StringComparison.OrdinalIgnoreCase)
      || host.EndsWith(".githubusercontent.com", StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>
  /// Send a GET request and follow redirects by hand. Each URL must pass <see cref="IsAllowedUrl"/>.
  /// The caller must dispose the response. The HttpClient must not follow redirects by itself.
  /// </summary>
  /// <exception cref="UpdateFailureException">A URL is not allowed, or there are too many redirects.</exception>
  public static async Task<HttpResponseMessage> GetAsync(
      HttpClient http,
      Uri url,
      HttpCompletionOption completion,
      UpdateError disallowedError,
      CancellationToken ct) {
    var current = url;
    for (var hop = 0; hop <= MaxRedirects; hop++) {
      if (!IsAllowedUrl(current)) {
        throw new UpdateFailureException(disallowedError);
      }

      using var request = new HttpRequestMessage(HttpMethod.Get, current);
      var response = await http.SendAsync(request, completion, ct).ConfigureAwait(false);
      var status = (int)response.StatusCode;
      if (status is < 300 or >= 400) {
        return response;
      }

      var location = response.Headers.Location;
      response.Dispose();
      if (location is null) {
        throw new UpdateFailureException(disallowedError);
      }

      current = location.IsAbsoluteUri ? location : new Uri(current, location);
    }

    throw new UpdateFailureException(disallowedError);
  }
}
