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
public sealed class AppConfig
{
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
    /// Maximum number of ADB retries per operation.
    /// Maps to <c>GAMEBOT_ADB_RETRIES</c> environment variable. Default 2.
    /// </summary>
    public int AdbRetries { get; set; } = 2;

    /// <summary>
    /// Delay in milliseconds between ADB retry attempts.
    /// Maps to <c>GAMEBOT_ADB_RETRY_DELAY_MS</c> environment variable. Default 100.
    /// </summary>
    public int AdbRetryDelayMs { get; set; } = 100;
}
