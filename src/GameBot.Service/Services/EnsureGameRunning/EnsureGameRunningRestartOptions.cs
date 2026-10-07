namespace GameBot.Service.Services.EnsureGameRunning;

/// <summary>
/// The time values of a forced restart (feature 129). The defaults are the production values.
/// Tests pass small values.
/// </summary>
internal sealed record EnsureGameRunningRestartOptions {
  /// <summary>Time to wait for the game to reach the foreground after the start.</summary>
  public TimeSpan RestartForegroundWait { get; init; } = TimeSpan.FromSeconds(30);

  /// <summary>Time between two foreground checks.</summary>
  public TimeSpan RestartPollInterval { get; init; } = TimeSpan.FromSeconds(1);

  /// <summary>Time to wait after the stop and before the start.</summary>
  public TimeSpan RestartSettleDelay { get; init; } = TimeSpan.FromSeconds(1);

  /// <summary>Time limit of the stop call.</summary>
  public TimeSpan RestartStopTimeout { get; init; } = TimeSpan.FromSeconds(10);

  /// <summary>Time limit of the start call.</summary>
  public TimeSpan RestartStartTimeout { get; init; } = TimeSpan.FromSeconds(10);

  /// <summary>Time limit of one foreground check.</summary>
  public TimeSpan RestartProbeTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
