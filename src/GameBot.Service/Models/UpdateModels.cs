namespace GameBot.Service.Models;

/// <summary>Answer (200) of <c>GET /api/update/status</c>.</summary>
internal sealed class UpdateStatusResponse {
  public string InstalledVersion { get; set; } = string.Empty;
  public UpdateCheckDto? LastCheck { get; set; }
  public UpdateAttemptDto? Attempt { get; set; }
  public UpdateAttemptDto? LastResult { get; set; }
  public bool CanInstallHere { get; set; }

  /// <summary><c>remote</c> or <c>notInstalled</c>. Null when the install is allowed.</summary>
  public string? InstallBlockedReason { get; set; }
}

/// <summary>Answer (200) of <c>POST /api/update/check</c>, and the <c>lastCheck</c> part of the status.</summary>
internal sealed class UpdateCheckDto {
  /// <summary><c>upToDate</c>, <c>updateAvailable</c>, or <c>checkFailed</c>.</summary>
  public string Status { get; set; } = string.Empty;
  public string InstalledVersion { get; set; } = string.Empty;
  public string? LatestVersion { get; set; }
  public string? Notes { get; set; }
  public DateTimeOffset CheckedAtUtc { get; set; }
  public UpdateErrorDto? Error { get; set; }
}

/// <summary>One update attempt.</summary>
internal sealed class UpdateAttemptDto {
  public string AttemptId { get; set; } = string.Empty;

  /// <summary><c>downloading</c>, <c>verifying</c>, <c>installing</c>, <c>restarting</c>, <c>succeeded</c>, or <c>failed</c>.</summary>
  public string State { get; set; } = string.Empty;
  public string TargetVersion { get; set; } = string.Empty;
  public string FromVersion { get; set; } = string.Empty;
  public DateTimeOffset StartedAtUtc { get; set; }
  public DateTimeOffset? FinishedAtUtc { get; set; }
  public string? ErrorCode { get; set; }
  public string? ErrorMessage { get; set; }
  public string? ErrorHint { get; set; }
  public int? MsiexecExitCode { get; set; }
  public string? LogPath { get; set; }
}

/// <summary>An error with a code, a message, and a fix hint.</summary>
internal sealed class UpdateErrorDto {
  public string Code { get; set; } = string.Empty;
  public string Message { get; set; } = string.Empty;
  public string? Hint { get; set; }
}

/// <summary>The error body of the update routes: <c>{ "error": { code, message, hint } }</c>.</summary>
internal sealed class UpdateErrorResponse {
  public UpdateErrorDto Error { get; set; } = new();
}

/// <summary>Request body of <c>POST /api/update/install</c>.</summary>
internal sealed class UpdateInstallRequest {
  public string? TargetVersion { get; set; }

  /// <summary>The user must confirm that all active queues stop at once. The value must be true.</summary>
  public bool ConfirmStopQueues { get; set; }
}

/// <summary>Answer (202) of <c>POST /api/update/install</c>.</summary>
internal sealed class UpdateInstallAccepted {
  public string AttemptId { get; set; } = string.Empty;
  public string State { get; set; } = string.Empty;
}
