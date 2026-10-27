namespace GameBot.Domain.Commands.Execution;

/// <summary>
/// The poll schedule of a <c>WaitForImage</c> step (feature 166, FR-010). The command executor and the
/// sequence runner use the same rule: no detection pass starts at or after the deadline, and the last
/// wait ends <see cref="LastPassLeadMs"/> before the deadline, so that the step does one last pass.
/// </summary>
public static class WaitForImageSchedule {
  /// <summary>The time in milliseconds from the end of the last wait to the deadline.</summary>
  public const int LastPassLeadMs = 50;

  private static readonly TimeSpan LastPassLead = TimeSpan.FromMilliseconds(LastPassLeadMs);

  /// <summary>
  /// Gives the next wait before a detection pass.
  /// <list type="bullet">
  ///   <item>More time remains than <paramref name="pollInterval"/>: wait one poll interval. This is not the last wait.</item>
  ///   <item>Not more than <paramref name="pollInterval"/> and more than 50 ms remain: wait the remaining time minus 50 ms. This is the last wait.</item>
  ///   <item>Not more than 50 ms and more than 0 ms remain: no wait. This is the last wait.</item>
  ///   <item>No time remains: stop. Do not start a pass.</item>
  /// </list>
  /// </summary>
  /// <param name="remaining">The time to the deadline.</param>
  /// <param name="pollInterval">The time between two passes.</param>
  public static WaitForImageWait NextWait(TimeSpan remaining, TimeSpan pollInterval) {
    if (remaining <= TimeSpan.Zero) return new WaitForImageWait(true, TimeSpan.Zero, true);
    if (remaining > pollInterval) return new WaitForImageWait(false, pollInterval, false);
    if (remaining > LastPassLead) return new WaitForImageWait(false, remaining - LastPassLead, true);
    return new WaitForImageWait(false, TimeSpan.Zero, true);
  }
}

/// <summary>One step of the <c>WaitForImage</c> poll schedule.</summary>
/// <param name="Stop">True when no time remains. The caller starts no pass.</param>
/// <param name="Wait">The time to wait before the next pass.</param>
/// <param name="IsLastWait">True when the pass after this wait is the last pass.</param>
public readonly record struct WaitForImageWait(bool Stop, TimeSpan Wait, bool IsLastWait);
