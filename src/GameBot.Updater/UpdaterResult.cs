using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameBot.Updater;

/// <summary>
/// The content of update-result.json. The shape is the same as UpdateAttempt in GameBot.Domain,
/// because the bot reads this file at start. The updater does not reference the domain project.
/// </summary>
internal sealed record UpdaterResult {
  public const string Installing = "installing";
  public const string Restarting = "restarting";
  public const string Succeeded = "succeeded";
  public const string Failed = "failed";
  public const string InstallFailedCode = "update_install_failed";
  public const string RestartFailedCode = "update_restart_failed";

  private static readonly JsonSerializerOptions Options = new() {
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = true
  };

  public string AttemptId { get; init; } = string.Empty;
  public string TargetVersion { get; init; } = string.Empty;
  public string FromVersion { get; init; } = string.Empty;
  public string State { get; init; } = string.Empty;
  public DateTimeOffset StartedAtUtc { get; init; }
  public DateTimeOffset? FinishedAtUtc { get; init; }
  public string? ErrorCode { get; init; }
  public string? ErrorMessage { get; init; }
  public string? ErrorHint { get; init; }
  public int? MsiexecExitCode { get; init; }
  public string? LogPath { get; init; }

  public string ToJson() => JsonSerializer.Serialize(this, Options);
}
