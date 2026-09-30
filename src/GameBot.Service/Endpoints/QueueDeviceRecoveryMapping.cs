using GameBot.Domain.Queues;
using GameBot.Service.Contracts.Queues;

namespace GameBot.Service.Endpoints;

/// <summary>
/// Validation and mapping for the device recovery settings of a queue (feature 121). It lives outside
/// <see cref="QueuesEndpoints"/> so the endpoint lambdas stay short, as the failure policy mapping does.
/// </summary>
internal static class QueueDeviceRecoveryMapping {
  /// <summary>
  /// Converts a request DTO to the domain record and validates it. Absent members take the default
  /// values. Returns true with <paramref name="recovery"/> set (null when <paramref name="dto"/> is null).
  /// Returns false with the first error text in <paramref name="error"/>.
  /// </summary>
  /// <param name="dto">The request member, or null.</param>
  /// <param name="emulatorInstanceName">The instance name of the queue, for the <c>reboot-instance</c> rule.</param>
  /// <param name="recovery">The domain settings, or null.</param>
  /// <param name="error">The error text, or null on success.</param>
  public static bool TryMap(
    QueueDeviceRecoveryDto? dto,
    string? emulatorInstanceName,
    out QueueDeviceRecovery? recovery,
    out string? error) {
    recovery = null;
    error = null;
    if (dto is null) return true;

    var mapped = new QueueDeviceRecovery {
      Action = string.IsNullOrWhiteSpace(dto.Action) ? QueueDeviceRecovery.ActionNone : dto.Action.Trim(),
      AfterMs = dto.AfterMs ?? QueueDeviceRecovery.DefaultAfterMs,
      MaxAttempts = dto.MaxAttempts ?? QueueDeviceRecovery.DefaultMaxAttempts,
      CooldownMs = dto.CooldownMs ?? QueueDeviceRecovery.DefaultCooldownMs
    };
    error = QueueDeviceRecoveryValidator.Validate(mapped, emulatorInstanceName);
    if (error is not null) return false;

    recovery = mapped;
    return true;
  }

  /// <summary>Projects the stored settings for a response, with the default values filled in. Null in, null out.</summary>
  public static QueueDeviceRecoveryDto? Project(QueueDeviceRecovery? recovery) =>
    recovery is null
      ? null
      : new QueueDeviceRecoveryDto {
        Action = recovery.Action,
        AfterMs = recovery.AfterMs,
        MaxAttempts = recovery.MaxAttempts,
        CooldownMs = recovery.CooldownMs
      };

  /// <summary>A value copy, so a duplicated queue does not share a mutable instance. Null in, null out.</summary>
  public static QueueDeviceRecovery? Clone(QueueDeviceRecovery? recovery) =>
    recovery is null
      ? null
      : new QueueDeviceRecovery {
        Action = recovery.Action,
        AfterMs = recovery.AfterMs,
        MaxAttempts = recovery.MaxAttempts,
        CooldownMs = recovery.CooldownMs
      };
}
