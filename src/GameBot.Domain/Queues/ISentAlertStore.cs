using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Domain.Queues;

/// <summary>
/// A claim on the alert slot of one (queue, key) scope (feature 137). The holder must call
/// <see cref="ISentAlertStore.CommitAsync"/> after a delivery, or <see cref="ISentAlertStore.ReleaseAsync"/>
/// after a failed delivery.
/// </summary>
public sealed class SentAlertReservation {
  /// <summary>Creates a reservation.</summary>
  public SentAlertReservation(string queueId, string key, DateTimeOffset reservedAt) {
    QueueId = queueId;
    Key = key;
    ReservedAt = reservedAt;
  }

  /// <summary>The queue identity, or <c>adhoc</c> for a run outside a queue.</summary>
  public string QueueId { get; }

  /// <summary>The trimmed dedupe key.</summary>
  public string Key { get; }

  /// <summary>The time of the claim. A claim older than 5 minutes is dead.</summary>
  public DateTimeOffset ReservedAt { get; }
}

/// <summary>
/// Keeps the records of delivered <c>notify</c> alerts, so a step with a dedupe key sends at most one
/// alert for each (queue, key) in a window (feature 137).
/// </summary>
public interface ISentAlertStore {
  /// <summary>
  /// Claims the slot of the scope. Returns null (suppressed) when the store has a record at or after
  /// <paramref name="windowStart"/>, or when another run holds a live claim.
  /// </summary>
  Task<SentAlertReservation?> TryReserveAsync(string queueId, string key, DateTimeOffset windowStart, DateTimeOffset now, CancellationToken ct = default);

  /// <summary>Turns the claim into a record with <paramref name="sentAt"/>. Writes the file.</summary>
  Task CommitAsync(SentAlertReservation reservation, DateTimeOffset sentAt, CancellationToken ct = default);

  /// <summary>Drops the claim after a failed delivery. The record stays as it was.</summary>
  Task ReleaseAsync(SentAlertReservation reservation, CancellationToken ct = default);

  /// <summary>Returns the time of the last delivered alert when it is at or after <paramref name="windowStart"/>. Read-only.</summary>
  Task<DateTimeOffset?> WasSentSinceAsync(string queueId, string key, DateTimeOffset windowStart, CancellationToken ct = default);

  /// <summary>True when a live, not expired claim exists for the scope. Read-only.</summary>
  Task<bool> HasLiveReservationAsync(string queueId, string key, DateTimeOffset now, CancellationToken ct = default);

  /// <summary>Removes all records and claims of the queue.</summary>
  Task DeleteQueueAsync(string queueId, CancellationToken ct = default);
}
