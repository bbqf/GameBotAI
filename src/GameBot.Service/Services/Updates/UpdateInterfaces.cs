using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;

namespace GameBot.Service.Services.Updates;

/// <summary>Reads the latest release from the release host.</summary>
internal interface IReleaseClient {
  /// <summary>Read the latest release and its manifest.</summary>
  /// <exception cref="UpdateFailureException">The read failed. The error code is one of the check codes of the update API.</exception>
  Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct);
}

/// <summary>Downloads the MSI and checks it against the manifest checksum.</summary>
internal interface IUpdateDownloader {
  /// <summary>Download the MSI to the updates folder. Return the file path. A failed download leaves no file.</summary>
  /// <exception cref="UpdateFailureException">Code <c>update_download_failed</c>.</exception>
  Task<string> DownloadAsync(ReleaseInfo release, CancellationToken ct);

  /// <summary>Compare the SHA-256 of the file with the manifest. A mismatch deletes the file.</summary>
  /// <exception cref="UpdateFailureException">Code <c>update_checksum_mismatch</c>.</exception>
  Task VerifyAsync(string msiPath, ReleaseInfo release, CancellationToken ct);
}

/// <summary>What the bot tells the updater program.</summary>
internal sealed record UpdaterLaunchRequest(
  Guid AttemptId,
  string MsiPath,
  string FromVersion,
  string TargetVersion,
  DateTimeOffset StartedAtUtc);

/// <summary>Starts the updater program as a detached process.</summary>
internal interface IUpdaterLauncher {
  /// <exception cref="UpdateFailureException">The updater did not start.</exception>
  void Launch(UpdaterLaunchRequest request);
}

/// <summary>Starts the updater process. The only part of the launcher that touches the operating system.</summary>
internal interface IUpdaterProcessStarter {
  /// <exception cref="InvalidOperationException">The process did not start.</exception>
  void StartDetached(string exePath, IReadOnlyList<string> arguments, string workingDirectory);
}

/// <summary>Reads and writes the result file of the last attempt.</summary>
internal interface IUpdateResultStore {
  Task<UpdateAttempt?> ReadAsync(CancellationToken ct);

  Task WriteAsync(UpdateAttempt attempt, CancellationToken ct);

  /// <summary>Rename the result file to <c>update-result.reported.json</c>.</summary>
  void MarkReported();

  /// <summary>Delete old downloads, the updater copy, and msiexec logs that are older than 30 days.</summary>
  void Cleanup(DateTimeOffset now);
}

/// <summary>The version of the running bot.</summary>
internal interface IInstalledVersionProvider {
  SemanticVersion GetInstalledVersion();
}

/// <summary>Tells if the bot runs from the folder that the installer made (FR-015).</summary>
internal interface IInstallLocationGuard {
  bool IsInstalledHere();
}

/// <summary>Free disk space for the update download.</summary>
internal interface IFreeSpaceProvider {
  /// <summary>Free bytes on the drive that holds <paramref name="path"/>. Return long.MaxValue when unknown.</summary>
  long GetFreeBytes(string path);
}

/// <summary>Stops all running queues before the bot exits for an update (FR-013).</summary>
internal interface IUpdateQueueStopper {
  Task StopAllAsync(CancellationToken ct);
}
