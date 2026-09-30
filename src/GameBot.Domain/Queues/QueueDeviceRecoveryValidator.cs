using System.Globalization;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// The range and cross-field rules of <see cref="QueueDeviceRecovery"/> (feature 121, FR-010).
  /// The error texts are the texts of the API contract.
  /// </summary>
  public static class QueueDeviceRecoveryValidator {
    /// <summary>
    /// Returns the first error text, or null when the settings are valid. Null settings are valid.
    /// The ranges are also checked when the action is <c>none</c>.
    /// </summary>
    /// <param name="recovery">The settings, or null.</param>
    /// <param name="emulatorInstanceName">The instance name of the queue. Needed by <c>reboot-instance</c>.</param>
    public static string? Validate(QueueDeviceRecovery? recovery, string? emulatorInstanceName) {
      if (recovery is null) return null;

      var action = recovery.Action;
      if (!string.Equals(action, QueueDeviceRecovery.ActionNone, System.StringComparison.Ordinal)
          && !string.Equals(action, QueueDeviceRecovery.ActionRebootInstance, System.StringComparison.Ordinal)) {
        return $"deviceRecovery.action must be one of: none, reboot-instance (was: '{action}')";
      }
      if (recovery.Reboots && string.IsNullOrWhiteSpace(emulatorInstanceName)) {
        return "deviceRecovery.action 'reboot-instance' needs emulatorInstanceName. Set emulatorInstanceName on the queue.";
      }
      if (recovery.AfterMs < QueueDeviceRecovery.MinAfterMs) {
        return $"deviceRecovery.afterMs must be at least {QueueDeviceRecovery.MinAfterMs} (was: {Text(recovery.AfterMs)})";
      }
      if (recovery.MaxAttempts < QueueDeviceRecovery.MinMaxAttempts || recovery.MaxAttempts > QueueDeviceRecovery.MaxMaxAttempts) {
        return $"deviceRecovery.maxAttempts must be from {QueueDeviceRecovery.MinMaxAttempts} to {QueueDeviceRecovery.MaxMaxAttempts} (was: {Text(recovery.MaxAttempts)})";
      }
      if (recovery.CooldownMs < QueueDeviceRecovery.MinCooldownMs) {
        return $"deviceRecovery.cooldownMs must be at least {QueueDeviceRecovery.MinCooldownMs} (was: {Text(recovery.CooldownMs)})";
      }
      return null;
    }

    private static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);
  }
}
