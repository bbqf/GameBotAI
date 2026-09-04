namespace GameBot.Domain.Config;

/// <summary>
/// Global application configuration settings for the GameBot domain layer.
/// <para>
/// <b>Tap Retry Algorithm</b>: Primitive tap steps use a wait-and-retry loop before execution.
/// The system waits <see cref="CaptureIntervalMs"/> before the initial detection check, then
/// retries up to <see cref="TapRetryCount"/> times. Between retries the wait time is multiplied
/// by <see cref="TapRetryProgression"/> (i.e. <c>nextWait = currentWait × TapRetryProgression</c>).
/// </para>
/// </summary>
public sealed class AppConfig {
  /// <summary>
  /// Global safety ceiling on the number of iterations a loop step may execute.
  /// Applied when no per-loop <see cref="Commands.LoopConfig.MaxIterations"/> override is
  /// set on the step.  Must be greater than zero.
  /// </summary>
  public int LoopMaxIterations { get; set; } = 1000;

  /// <summary>
  /// Base wait time in milliseconds between retry cycles and the initial detection check.
  /// Also used by <c>BackgroundScreenCaptureService</c> as the capture interval.
  /// Maps to <c>GAMEBOT_CAPTURE_INTERVAL_MS</c> environment variable.
  /// Clamped to a minimum of 50 ms at startup.
  /// </summary>
  public int CaptureIntervalMs { get; set; } = 500;

  /// <summary>
  /// Maximum number of retry cycles for primitive tap image detection.
  /// <c>0</c> = single detection check with no retries; negative values fall back to default 3.
  /// Maps to <c>GAMEBOT_TAP_RETRY_COUNT</c> environment variable.
  /// </summary>
  public int TapRetryCount { get; set; } = 3;

  /// <summary>
  /// Multiplier applied to the wait time after each unsuccessful retry cycle.
  /// <c>1.0</c> = constant interval; values &gt; 1 create exponential backoff.
  /// Must be positive (&gt; 0); invalid values fall back to default 1.0.
  /// Maps to <c>GAMEBOT_TAP_RETRY_PROGRESSION</c> environment variable.
  /// </summary>
  public double TapRetryProgression { get; set; } = 1.0;

  /// <summary>
  /// Maximum random per-axis offset in pixels applied independently to the X and Y coordinate
  /// of every tap and swipe endpoint immediately before it is sent to the device.
  /// <c>0</c> disables jitter (coordinates pass through unchanged); negative values fall back
  /// to the default 5. Maps to <c>GAMEBOT_TAP_JITTER_RADIUS_PX</c> environment variable.
  /// </summary>
  public int TapJitterRadiusPx { get; set; } = 5;

  /// <summary>
  /// Maximum number of ADB retries per operation.
  /// Maps to <c>GAMEBOT_ADB_RETRIES</c> environment variable. Default 2.
  /// </summary>
  public int AdbRetries { get; set; } = 2;

  /// <summary>
  /// Delay in milliseconds between ADB retry attempts.
  /// Maps to <c>GAMEBOT_ADB_RETRY_DELAY_MS</c> environment variable. Default 100.
  /// </summary>
  public int AdbRetryDelayMs { get; set; } = 100;

  /// <summary>
  /// Timeout in milliseconds for a single emulator responsiveness probe
  /// (<c>getprop sys.boot_completed</c>) used by the ensure-emulator-running action (feature 070).
  /// Maps to <c>GAMEBOT_EMULATOR_PROBE_TIMEOUT_MS</c>. Default 10000. Binders clamp to a small minimum
  /// and fall back to the default on invalid/non-numeric values.
  /// </summary>
  public int EmulatorProbeTimeoutMs { get; set; } = 10000;

  /// <summary>
  /// Maximum time in milliseconds to wait for an emulator instance to reach boot-complete after a
  /// start or restart (feature 070). Maps to <c>GAMEBOT_EMULATOR_BOOT_WAIT_MS</c>. Default 120000.
  /// Binders clamp this to be at least <see cref="EmulatorProbeTimeoutMs"/> and fall back to the
  /// default on invalid values.
  /// </summary>
  public int EmulatorBootWaitMs { get; set; } = 120000;

  /// <summary>
  /// Interval in milliseconds between health polls while waiting for an emulator to become healthy
  /// after a (re)start (feature 070). Maps to <c>GAMEBOT_EMULATOR_POLL_INTERVAL_MS</c>. Default 3000.
  /// Binders clamp to a minimum of 100 and fall back to the default on invalid values.
  /// </summary>
  public int EmulatorPollIntervalMs { get; set; } = 3000;

  /// <summary>
  /// How often, in milliseconds, to re-check that every running queue's emulator is still answering
  /// ADB. Maps to <c>GAMEBOT_QUEUE_DEVICE_WATCHDOG_INTERVAL_MS</c>. Default 60000; <c>0</c> disables
  /// the watchdog entirely.
  /// <para>
  /// An emulator can die mid-run without anything noticing: the session keeps reporting healthy and
  /// the queue keeps firing sequences whose input goes nowhere, so a run can burn hours achieving
  /// nothing. Only a queue start brings an instance back up, so the watchdog restarts the run.
  /// </para>
  /// </summary>
  public int QueueDeviceWatchdogIntervalMs { get; set; } = 60000;

  /// <summary>
  /// Consecutive failed probes before the watchdog restarts a queue's run. Maps to
  /// <c>GAMEBOT_QUEUE_DEVICE_WATCHDOG_STRIKES</c>. Default 3. More than one is required because ADB
  /// drops a device briefly for reasons that resolve on their own, and a restart costs the run its
  /// live self-reschedules.
  /// </summary>
  public int QueueDeviceWatchdogStrikes { get; set; } = 3;

  /// <summary>
  /// Minimum time in milliseconds between two watchdog restarts of the same queue. Maps to
  /// <c>GAMEBOT_QUEUE_DEVICE_WATCHDOG_COOLDOWN_MS</c>. Default 600000. Stops a host whose emulator
  /// cannot come back from being restarted in a tight loop.
  /// </summary>
  public int QueueDeviceWatchdogCooldownMs { get; set; } = 600000;
}
