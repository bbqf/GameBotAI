using System;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The pause a run is in at one instant (feature 096, issue #199), as projected onto
/// <c>health.paused</c>, <c>pausedAt</c>, <c>pauseReason</c> and <c>pauseKind</c>. The default value
/// means "not paused": false with every other field null.
/// </summary>
/// <param name="Paused">Whether any pause is in force.</param>
/// <param name="PausedAt">When the reported pause began (local clock); null when not paused.</param>
/// <param name="Reason">Human-readable reason; null when not paused.</param>
/// <param name="Kind">One of <see cref="QueuePauseKinds"/>; null when not paused.</param>
internal readonly record struct QueuePauseSnapshot(bool Paused, DateTimeOffset? PausedAt, string? Reason, string? Kind);

/// <summary>Wire values of <c>health.pauseKind</c>.</summary>
internal static class QueuePauseKinds {
  /// <summary>Feature 073's routine, self-releasing hold between firings.</summary>
  public const string Idle = "idle";

  /// <summary>Feature 087's park after a tripped <c>pause</c> policy; released only by an explicit resume.</summary>
  public const string FailurePolicy = "failurePolicy";
}
