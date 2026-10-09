using GameBot.Domain.Updates;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Downloads the MSI with HttpClient inside the bot process. A file that HttpClient writes has no
/// Mark of the Web, so Windows shows no SmartScreen prompt (research R-003). A failed download
/// deletes the partial file (FR-009).
/// </summary>
internal sealed class UpdateDownloader : IUpdateDownloader {
  private const int BufferSize = 81920;

  private readonly HttpClient _http;
  private readonly UpdatePaths _paths;

  public UpdateDownloader(HttpClient http, UpdatePaths paths) {
    _http = http;
    _paths = paths;
  }

  public async Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(release);
    Directory.CreateDirectory(_paths.Directory);
    var target = _paths.MsiPath(release.Version.ToString());
    TryDelete(target);

    try {
      var disallowed = new UpdateError("update_download_failed", "The installer is on a host that the bot does not trust.", "Ask the owner to publish the release again.");
      using var response = await UpdateHttp.GetAsync(_http, release.MsiUrl, HttpCompletionOption.ResponseHeadersRead, disallowed, ct).ConfigureAwait(false);
      if (!response.IsSuccessStatusCode) {
        throw DownloadFailure($"GitHub answered with HTTP {(int)response.StatusCode}.", null);
      }

      var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
      await using (source.ConfigureAwait(false)) {
        var file = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
        await using (file.ConfigureAwait(false)) {
          await source.CopyToAsync(file, BufferSize, ct).ConfigureAwait(false);
        }
      }

      if (new FileInfo(target).Length != release.MsiSizeBytes) {
        throw DownloadFailure("The downloaded file has the wrong size.", null);
      }

      return target;
    }
    catch (UpdateFailureException) {
      TryDelete(target);
      throw;
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
      TryDelete(target);
      throw;
    }
    catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException) {
      TryDelete(target);
      throw DownloadFailure("The download stopped before the end.", ex);
    }
  }

  public async Task VerifyAsync(string msiPath, ReleaseInfo release, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(release);
    bool match;
    var file = new FileStream(msiPath, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
    await using (file.ConfigureAwait(false)) {
      match = await ChecksumVerifier.MatchesAsync(file, release.MsiSha256, ct).ConfigureAwait(false);
    }

    if (!match) {
      TryDelete(msiPath);
      throw new UpdateFailureException(new UpdateError(
        "update_checksum_mismatch",
        "The downloaded installer does not match its checksum.",
        "Try the update again. If it fails again, ask the owner to publish the release again."));
    }
  }

  private static UpdateFailureException DownloadFailure(string message, Exception? inner) {
    var error = new UpdateError("update_download_failed", message, "Check the internet connection, then try again.");
    return inner is null ? new UpdateFailureException(error) : new UpdateFailureException(error, inner);
  }

  private static void TryDelete(string path) {
    try {
      if (File.Exists(path)) {
        File.Delete(path);
      }
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // The next download replaces the file. A leftover file does no harm.
    }
  }
}
