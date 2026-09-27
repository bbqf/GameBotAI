namespace GameBot.Domain.Sessions;

/// <summary>
/// The limits of the device liveness check (feature 106, issue #220). All values are in milliseconds.
/// The configuration section is <see cref="SectionName"/>. Use <see cref="Normalized"/> before you read
/// a value: it sets each value to at least its minimum.
/// </summary>
public sealed class DeviceLivenessOptions {
  /// <summary>The configuration section of these options.</summary>
  public const string SectionName = "Service:DeviceLiveness";

  /// <summary>Minimum of <see cref="StaleLimitMs"/>.</summary>
  public const int MinStaleLimitMs = 1000;

  /// <summary>Minimum of <see cref="CaptureStallLimitMs"/>.</summary>
  public const int MinCaptureStallLimitMs = 1000;

  /// <summary>Minimum of <see cref="InputTimeoutMs"/>.</summary>
  public const int MinInputTimeoutMs = 100;

  /// <summary>Minimum of <see cref="CaptureTimeoutMs"/>.</summary>
  public const int MinCaptureTimeoutMs = 100;

  /// <summary>Minimum of <see cref="TransportCheckTimeoutMs"/>.</summary>
  public const int MinTransportCheckTimeoutMs = 100;

  /// <summary>Minimum of <see cref="QueueGracePeriodMs"/>.</summary>
  public const int MinQueueGracePeriodMs = 0;

  /// <summary>Minimum of <see cref="QueueCheckIntervalMs"/>.</summary>
  public const int MinQueueCheckIntervalMs = 1000;

  /// <summary>
  /// The frame is stale when its bytes did not change for longer than this value. Default 300000 (5 min).
  /// </summary>
  public int StaleLimitMs { get; set; } = 300000;

  /// <summary>
  /// The capture is stalled when no capture completed for longer than this value. Default 60000 (60 s).
  /// </summary>
  public int CaptureStallLimitMs { get; set; } = 60000;

  /// <summary>The time limit of one input action. Default 10000 (10 s).</summary>
  public int InputTimeoutMs { get; set; } = 10000;

  /// <summary>
  /// The time limit of one direct capture and of one capture of the capture loop. Default 10000 (10 s).
  /// </summary>
  public int CaptureTimeoutMs { get; set; } = 10000;

  /// <summary>The time limit of <c>adb get-state</c> in the health call. Default 5000 (5 s).</summary>
  public int TransportCheckTimeoutMs { get; set; } = 5000;

  /// <summary>
  /// A queue records the fault episode when the device stays not live for longer than this value.
  /// Default 120000 (2 min).
  /// </summary>
  public int QueueGracePeriodMs { get; set; } = 120000;

  /// <summary>
  /// The interval of the queue liveness check. It is also the wait of the queue after a held firing.
  /// Default 30000 (30 s).
  /// </summary>
  public int QueueCheckIntervalMs { get; set; } = 30000;

  /// <summary>
  /// Returns a copy in which each value is at least its minimum. This method does not change this
  /// object, and it never throws.
  /// </summary>
  public DeviceLivenessOptions Normalized() => new() {
    StaleLimitMs = Math.Max(MinStaleLimitMs, StaleLimitMs),
    CaptureStallLimitMs = Math.Max(MinCaptureStallLimitMs, CaptureStallLimitMs),
    InputTimeoutMs = Math.Max(MinInputTimeoutMs, InputTimeoutMs),
    CaptureTimeoutMs = Math.Max(MinCaptureTimeoutMs, CaptureTimeoutMs),
    TransportCheckTimeoutMs = Math.Max(MinTransportCheckTimeoutMs, TransportCheckTimeoutMs),
    QueueGracePeriodMs = Math.Max(MinQueueGracePeriodMs, QueueGracePeriodMs),
    QueueCheckIntervalMs = Math.Max(MinQueueCheckIntervalMs, QueueCheckIntervalMs)
  };
}
