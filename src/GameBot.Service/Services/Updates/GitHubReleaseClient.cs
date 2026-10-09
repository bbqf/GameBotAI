using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Text.Json;
using GameBot.Domain.Updates;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Reads the latest public release from the GitHub REST API and its update-manifest.json (research R-001).
/// The client sends no token. Every failure becomes an <see cref="UpdateFailureException"/> with a code
/// from the update API contract.
/// </summary>
internal sealed partial class GitHubReleaseClient : IReleaseClient {
  public const string ManifestAssetName = "update-manifest.json";

  private readonly HttpClient _http;
  private readonly UpdateOptions _options;
  private readonly ILogger<GitHubReleaseClient> _logger;

  public GitHubReleaseClient(HttpClient http, IOptions<UpdateOptions> options, ILogger<GitHubReleaseClient> logger) {
    _http = http;
    _options = options.Value;
    _logger = logger;
  }

  [GeneratedRegex("^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")]
  private static partial Regex RepositoryPattern();

  public async Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct) {
    if (!RepositoryPattern().IsMatch(_options.Repository)) {
      throw Failure("update_no_release", "The update repository setting is not valid.", "Set Update:Repository to owner/name.");
    }

    try {
      var json = await ReadLatestReleaseJsonAsync(ct).ConfigureAwait(false);
      using var document = JsonDocument.Parse(json);
      return await BuildReleaseAsync(document.RootElement, ct).ConfigureAwait(false);
    }
    catch (HttpRequestException ex) {
      throw NetworkFailure(ex);
    }
    catch (TaskCanceledException ex) when (!ct.IsCancellationRequested) {
      throw NetworkFailure(ex);
    }
    catch (JsonException ex) {
      throw new UpdateFailureException(
        new UpdateError("update_no_release", "GitHub sent a release that the bot cannot read.", "Try again later."), ex);
    }
  }

  private async Task<string> ReadLatestReleaseJsonAsync(CancellationToken ct) {
    var url = new Uri(_options.ApiBaseUrl, $"/repos/{_options.Repository}/releases/latest");
    using var request = new HttpRequestMessage(HttpMethod.Get, url);
    request.Headers.Accept.ParseAdd("application/vnd.github+json");
    using var response = await _http.SendAsync(request, ct).ConfigureAwait(false);

    if (response.StatusCode == HttpStatusCode.NotFound) {
      throw Failure("update_no_release", "The repository has no release.", "Ask the owner to publish a release.");
    }
    if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests) {
      throw Failure("update_rate_limited", "GitHub refused the request because of the request limit.", "Try again later.");
    }
    if (!response.IsSuccessStatusCode) {
      throw Failure("update_network_error", $"GitHub answered with HTTP {(int)response.StatusCode}.", "Try again later.");
    }

    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
  }

  private async Task<ReleaseInfo> BuildReleaseAsync(JsonElement root, CancellationToken ct) {
    var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
    if (!UpdateVersionSelector.TryParseTag(tag, out var version)) {
      UpdateLog.TagIgnored(_logger, tag ?? string.Empty);
      throw Failure("update_no_release", "The latest release has no version tag that the bot can read.", "Ask the owner to publish a release.");
    }

    var assets = ReadAssets(root);
    if (!assets.TryGetValue(ManifestAssetName, out var manifestUrl)) {
      throw Failure("update_manifest_invalid", "The release has no update manifest.", "Ask the owner to publish the release again.");
    }

    var manifestJson = await ReadManifestTextAsync(manifestUrl, ct).ConfigureAwait(false);
    UpdateManifest manifest;
    try {
      manifest = UpdateManifest.Parse(manifestJson, version);
    }
    catch (UpdateManifestException ex) {
      var hint = ex.Code == UpdateManifest.UnsupportedCode
        ? "Install the newest version by hand from the release page."
        : "Ask the owner to publish the release again.";
      throw new UpdateFailureException(new UpdateError(ex.Code, ex.Message, hint), ex);
    }

    if (!assets.TryGetValue(manifest.MsiFileName, out var msiUrl) || !UpdateHttp.IsAllowedUrl(msiUrl)) {
      throw Failure("update_manifest_invalid", "The release has no installer file that the bot may download.", "Ask the owner to publish the release again.");
    }

    var published = root.TryGetProperty("published_at", out var publishedElement)
      && DateTimeOffset.TryParse(publishedElement.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
        ? parsed
        : DateTimeOffset.UtcNow;
    var notes = root.TryGetProperty("body", out var bodyElement) && bodyElement.ValueKind == JsonValueKind.String
      ? bodyElement.GetString()
      : null;

    return new ReleaseInfo(
      version,
      tag!,
      published,
      string.IsNullOrWhiteSpace(notes) ? null : notes,
      msiUrl,
      manifest.MsiSizeBytes,
      manifest.MsiSha256);
  }

  private async Task<string> ReadManifestTextAsync(Uri manifestUrl, CancellationToken ct) {
    var disallowed = new UpdateError("update_manifest_invalid", "The manifest is on a host that the bot does not trust.", "Ask the owner to publish the release again.");
    using var response = await UpdateHttp.GetAsync(_http, manifestUrl, HttpCompletionOption.ResponseContentRead, disallowed, ct).ConfigureAwait(false);
    if (!response.IsSuccessStatusCode) {
      throw Failure("update_manifest_invalid", $"The bot cannot read the update manifest (HTTP {(int)response.StatusCode}).", "Try again later.");
    }

    return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
  }

  private static Dictionary<string, Uri> ReadAssets(JsonElement root) {
    var result = new Dictionary<string, Uri>(StringComparer.OrdinalIgnoreCase);
    if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array) {
      return result;
    }

    foreach (var asset in assets.EnumerateArray()) {
      var name = asset.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : null;
      var urlText = asset.TryGetProperty("browser_download_url", out var urlElement) ? urlElement.GetString() : null;
      if (!string.IsNullOrEmpty(name) && Uri.TryCreate(urlText, UriKind.Absolute, out var url)) {
        result[name] = url;
      }
    }

    return result;
  }

  private static UpdateFailureException Failure(string code, string message, string hint) =>
    new(new UpdateError(code, message, hint));

  private static UpdateFailureException NetworkFailure(Exception ex) =>
    new(new UpdateError("update_network_error", "The bot cannot reach GitHub.", "Check the internet connection, then try again."), ex);
}
