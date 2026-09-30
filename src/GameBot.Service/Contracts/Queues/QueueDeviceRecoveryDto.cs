namespace GameBot.Service.Contracts.Queues {
  /// <summary>
  /// Request and response shape of the device recovery settings of a queue (feature 121, contract
  /// <c>device-recovery-api.md</c>). A member that is absent in a request takes its default value.
  /// </summary>
  internal sealed class QueueDeviceRecoveryDto {
    /// <summary><c>none</c> (default) or <c>reboot-instance</c>. Another value gives HTTP 400.</summary>
    public string? Action { get; set; }

    /// <summary>The age of one not-live episode after which the first attempt starts. Default 300000. Minimum 60000.</summary>
    public int? AfterMs { get; set; }

    /// <summary>The count of attempts for one episode. Default 2. From 1 to 5.</summary>
    public int? MaxAttempts { get; set; }

    /// <summary>The least time between two attempts. Default 180000. Minimum 0.</summary>
    public int? CooldownMs { get; set; }
  }
}
