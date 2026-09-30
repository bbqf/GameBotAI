namespace GameBot.Domain.Queues {
  /// <summary>
  /// Optional, persisted settings for the recovery of a device that stays not live (feature 121,
  /// issue #261). Null on <see cref="ExecutionQueue.DeviceRecovery"/> is the same as
  /// <see cref="ActionNone"/>: the queue only sends the alert and never repairs the device.
  /// </summary>
  public class QueueDeviceRecovery {
    /// <summary>The action value that turns recovery off.</summary>
    public const string ActionNone = "none";

    /// <summary>The action value that restarts the LDPlayer instance of the queue.</summary>
    public const string ActionRebootInstance = "reboot-instance";

    /// <summary>The default of <see cref="AfterMs"/> (5 min).</summary>
    public const int DefaultAfterMs = 300000;

    /// <summary>The minimum of <see cref="AfterMs"/> (1 min).</summary>
    public const int MinAfterMs = 60000;

    /// <summary>The default of <see cref="MaxAttempts"/>.</summary>
    public const int DefaultMaxAttempts = 2;

    /// <summary>The minimum of <see cref="MaxAttempts"/>.</summary>
    public const int MinMaxAttempts = 1;

    /// <summary>The maximum of <see cref="MaxAttempts"/>.</summary>
    public const int MaxMaxAttempts = 5;

    /// <summary>The default of <see cref="CooldownMs"/> (3 min).</summary>
    public const int DefaultCooldownMs = 180000;

    /// <summary>The minimum of <see cref="CooldownMs"/>.</summary>
    public const int MinCooldownMs = 0;

    /// <summary>One of <see cref="ActionNone"/> and <see cref="ActionRebootInstance"/>.</summary>
    public string Action { get; set; } = ActionNone;

    /// <summary>The age of one not-live episode after which the first attempt starts. Minimum 60000.</summary>
    public int AfterMs { get; set; } = DefaultAfterMs;

    /// <summary>The count of attempts for one episode. From 1 to 5.</summary>
    public int MaxAttempts { get; set; } = DefaultMaxAttempts;

    /// <summary>The least time between the end of one attempt and the start of the next. Minimum 0.</summary>
    public int CooldownMs { get; set; } = DefaultCooldownMs;

    /// <summary>True when <see cref="Action"/> is <see cref="ActionRebootInstance"/>.</summary>
    public bool Reboots =>
      string.Equals(Action, ActionRebootInstance, System.StringComparison.Ordinal);
  }
}
