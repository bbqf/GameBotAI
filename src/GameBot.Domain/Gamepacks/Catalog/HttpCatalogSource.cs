using System.Net;
using System.Net.Http.Headers;

namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>
/// A catalog on a web server (feature 147). The client MUST NOT follow redirects by itself (<c>AllowAutoRedirect = false</c>).
/// This source follows at most a fixed number of redirects by hand, only to the same scheme, host, and port. It sends the bearer
/// token only to the catalog host. Each request has a time limit and a counted size limit.
/// </summary>
public sealed class HttpCatalogSource : IGamepackCatalogSource {
  private readonly HttpClient _client;
  private readonly Uri _base;
  private readonly string? _token;
  private readonly CatalogOptions _options;

  /// <summary>Creates the source.</summary>
  /// <param name="client">The client. Redirects must be off.</param>
  /// <param name="baseUri">The base address with a trailing slash.</param>
  /// <param name="token">The bearer token, or null.</param>
  /// <param name="options">The limits.</param>
  public HttpCatalogSource(HttpClient client, Uri baseUri, string? token, CatalogOptions options) {
    _client = client ?? throw new ArgumentNullException(nameof(client));
    _base = baseUri ?? throw new ArgumentNullException(nameof(baseUri));
    _token = string.IsNullOrEmpty(token) ? null : token;
    _options = options ?? throw new ArgumentNullException(nameof(options));
  }

  /// <inheritdoc />
  public Task<byte[]> ReadIndexAsync(CancellationToken ct) =>
    GetAsync(new Uri(_base, CatalogLocation.IndexName), _options.IndexLimitBytes, isIndex: true, ct);

  /// <inheritdoc />
  public Task<byte[]> ReadFileAsync(string location, long limitBytes, CancellationToken ct) {
    var uri = CatalogLocation.ResolveWeb(_base, location)
      ?? throw new CatalogException(CatalogFailure.FileMissing, "The file location is not under the catalog address.");
    return GetAsync(uri, limitBytes, isIndex: false, ct);
  }

  private async Task<byte[]> GetAsync(Uri uri, long limit, bool isIndex, CancellationToken ct) {
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
    timeout.CancelAfter(_options.RequestTimeout);
    try {
      return await SendAsync(uri, limit, isIndex, timeout.Token).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
      throw new CatalogException(CatalogFailure.Timeout, "The catalog did not answer in time.");
    }
    catch (HttpRequestException ex) {
      throw new CatalogException(CatalogFailure.Unreachable, "The catalog cannot be reached.", ex);
    }
    catch (IOException ex) {
      throw new CatalogException(CatalogFailure.Unreachable, "The catalog connection failed.", ex);
    }
  }

  private async Task<byte[]> SendAsync(Uri start, long limit, bool isIndex, CancellationToken ct) {
    var tooLarge = isIndex ? CatalogFailure.IndexTooLarge : CatalogFailure.FileTooLarge;
    var current = start;
    for (var hop = 0; ; hop++) {
      using var request = new HttpRequestMessage(HttpMethod.Get, current);
      if (_token is not null && IsCatalogHost(current)) {
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
      }

      using var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
      if (IsRedirect(response.StatusCode)) {
        current = NextHop(response, current, hop);
        continue;
      }

      ThrowForStatus(response.StatusCode, isIndex);
      if (response.Content.Headers.ContentLength is { } length && length > limit) {
        throw new CatalogException(tooLarge, "The file is larger than the size limit.");
      }

      return await ReadCountedAsync(response, limit, tooLarge, ct).ConfigureAwait(false);
    }
  }

  private Uri NextHop(HttpResponseMessage response, Uri current, int hop) {
    var location = response.Headers.Location;
    if (location is null || hop >= _options.MaxRedirects) {
      throw new CatalogException(CatalogFailure.RedirectRefused, "The catalog redirect is not allowed.");
    }

    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
    if (!IsCatalogHost(next)) {
      throw new CatalogException(CatalogFailure.RedirectRefused, "The catalog redirect goes to another host. It is refused.");
    }

    return next;
  }

  private bool IsCatalogHost(Uri uri) =>
    string.Equals(uri.Scheme, _base.Scheme, StringComparison.OrdinalIgnoreCase)
    && string.Equals(uri.Host, _base.Host, StringComparison.OrdinalIgnoreCase)
    && uri.Port == _base.Port;

  private static bool IsRedirect(HttpStatusCode status) =>
    status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
      or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;

  private static void ThrowForStatus(HttpStatusCode status, bool isIndex) {
    if ((int)status is >= 200 and < 300) {
      return;
    }

    throw status switch {
      HttpStatusCode.Unauthorized => new CatalogException(CatalogFailure.Unauthorized, "The catalog refused the token."),
      HttpStatusCode.Forbidden => new CatalogException(CatalogFailure.Forbidden, "The catalog denied access."),
      HttpStatusCode.NotFound when isIndex => new CatalogException(CatalogFailure.IndexInvalid, "The catalog has no index.json."),
      HttpStatusCode.NotFound => new CatalogException(CatalogFailure.NotFound, "The catalog has no file at the listed location."),
      _ => new CatalogException(CatalogFailure.Unreachable, "The catalog answered with an error status.")
    };
  }

  private static async Task<byte[]> ReadCountedAsync(HttpResponseMessage response, long limit, string tooLarge, CancellationToken ct) {
    using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0) {
      if (buffer.Length + read > limit) {
        throw new CatalogException(tooLarge, "The file is larger than the size limit.");
      }

      await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
    }

    return buffer.ToArray();
  }
}
