using System;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// One emulator device exclusively reserved by one queue run (feature 079).
/// </summary>
/// <param name="DeviceSerial">The normalized (trimmed) ADB serial that is claimed.</param>
/// <param name="QueueId">The queue whose run holds the claim.</param>
/// <param name="QueueName">The holding queue's display name, for the refusal message.</param>
/// <param name="ClaimedAtUtc">When the claim was taken; diagnostics only.</param>
internal sealed record DeviceClaim(
  string DeviceSerial,
  string QueueId,
  string QueueName,
  DateTimeOffset ClaimedAtUtc);

/// <summary>
/// Exclusive, in-memory ownership of emulator devices by queue runs (feature 079, FR-008..FR-014).
/// </summary>
/// <remarks>
/// Two automations driving one screen cannot both be correct, so at most one run may hold a given
/// device serial at a time. Feature 051 FR-013 previously allowed same-emulator concurrency and made
/// interference the operator's problem; this registry reverses that. Claims are never persisted, so a
/// service restart voids them all. Mirrors <see cref="IQueueRunRegistry"/> in shape and lifetime.
/// </remarks>
internal interface IDeviceClaimRegistry {
  /// <summary>
  /// Atomically claims <paramref name="deviceSerial"/> for a queue run.
  /// </summary>
  /// <param name="deviceSerial">
  /// The queue's bound ADB serial. Trimmed and compared case-insensitively. A blank serial has no
  /// device identity to reserve, so it is accepted as a no-op claim and never blocks another queue.
  /// </param>
  /// <param name="queueId">The claiming queue.</param>
  /// <param name="queueName">The claiming queue's display name.</param>
  /// <returns>
  /// <c>true</c> when the claim was taken (or was a blank-serial no-op); <c>false</c> when a
  /// different queue already holds the device.
  /// </returns>
  bool TryClaim(string? deviceSerial, string queueId, string queueName);

  /// <summary>
  /// Releases the claim on <paramref name="deviceSerial"/>, but only when <paramref name="queueId"/>
  /// still holds it, so a late release from a finished run cannot steal a newer run's claim.
  /// Idempotent; a blank serial or an unheld device is a no-op.
  /// </summary>
  void Release(string? deviceSerial, string queueId);

  /// <summary>Looks up the current holder of a device, for building the refusal message.</summary>
  /// <returns><c>false</c> when the serial is blank or unclaimed.</returns>
  bool TryGetHolder(string? deviceSerial, out DeviceClaim claim);
}
