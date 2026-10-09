using GameBot.Domain.Versioning;

namespace GameBot.Domain.Updates;

/// <summary>Result of a check for an update.</summary>
public enum UpdateCheckStatus {
  UpToDate,
  UpdateAvailable,
  CheckFailed
}

/// <summary>State of an update attempt. The order follows the state diagram in the data model.</summary>
public enum UpdateState {
  Idle,
  Downloading,
  Verifying,
  Installing,
  Restarting,
  Succeeded,
  Failed
}

/// <summary>An error with a code, a message, and an optional fix hint.</summary>
public sealed record UpdateError(string Code, string Message, string? Hint);

/// <summary>A release that the bot read from GitHub. The bot does not store it.</summary>
public sealed record ReleaseInfo(
  SemanticVersion Version,
  string Tag,
  DateTimeOffset PublishedAtUtc,
  string? Notes,
  Uri MsiUrl,
  long MsiSizeBytes,
  string MsiSha256);

/// <summary>The result of one check. The status is UpdateAvailable only when the release is newer.</summary>
public sealed record UpdateCheckResult(
  UpdateCheckStatus Status,
  SemanticVersion InstalledVersion,
  SemanticVersion? LatestVersion,
  string? Notes,
  DateTimeOffset CheckedAtUtc,
  UpdateError? Error);

/// <summary>
/// One update attempt. The updater program writes the final state to update-result.json.
/// Versions are text so the file stays easy to read and to write from the updater.
/// </summary>
public sealed record UpdateAttempt {
  public Guid AttemptId { get; init; }
  public string TargetVersion { get; init; } = string.Empty;
  public string FromVersion { get; init; } = string.Empty;
  public UpdateState State { get; init; }
  public DateTimeOffset StartedAtUtc { get; init; }
  public DateTimeOffset? FinishedAtUtc { get; init; }
  public string? ErrorCode { get; init; }
  public string? ErrorMessage { get; init; }
  public string? ErrorHint { get; init; }
  public int? MsiexecExitCode { get; init; }
  public string? LogPath { get; init; }
}
