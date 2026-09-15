using System;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Service.Services.Notifications {
  /// <summary>Outcome of one notification delivery attempt sequence.</summary>
  /// <param name="Succeeded">True iff a destination was resolved and answered 2xx within the budget.</param>
  /// <param name="CompletedAt">When the attempt sequence finished (local clock).</param>
  /// <param name="Error">
  /// Failure detail when <paramref name="Succeeded"/> is false; null on success. Safe to surface to
  /// an operator — it never contains the configured auth header value or the response body.
  /// </param>
  internal readonly record struct FailureNotificationResult(
    bool Succeeded,
    DateTimeOffset CompletedAt,
    string? Error);

  /// <summary>
  /// Delivers a <see cref="FailureNotificationEvent"/> to the configured destination
  /// (feature 087, issue #181).
  /// <para>
  /// <b>Implementations MUST NOT throw.</b> This is a load-bearing contract, not an aspiration: the
  /// caller is the queue run loop that drives production farms, and a notification about a failure
  /// must never itself become a failure. Every fault — an unresolvable destination, a refused
  /// connection, a timeout, a malformed URL — is reported through
  /// <see cref="FailureNotificationResult.Error"/>, never as an exception.
  /// </para>
  /// <para>
  /// Delivery is also bounded: a fixed per-attempt timeout and a fixed attempt count, so a receiver
  /// that never answers cannot keep a delivery alive indefinitely.
  /// </para>
  /// </summary>
  internal interface IFailureNotifier {
    /// <summary>
    /// Delivers <paramref name="evt"/>, resolving the destination as
    /// <paramref name="overrideUrl"/> when supplied, otherwise the service-wide default.
    /// </summary>
    /// <param name="evt">The event to deliver.</param>
    /// <param name="overrideUrl">Per-policy or per-step destination; null to use the default.</param>
    /// <param name="ct">
    /// Cancellation for the delivery itself. Callers stopping a run MUST NOT pass that run's token:
    /// a <c>notifyAndStop</c> alert would then be cancelled by the very stop it announces.
    /// </param>
    /// <returns>The outcome. Never throws, and never returns a faulted task.</returns>
    Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt,
      string? overrideUrl,
      CancellationToken ct = default);
  }
}
