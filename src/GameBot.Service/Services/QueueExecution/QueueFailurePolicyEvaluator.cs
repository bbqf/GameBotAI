using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Service.Endpoints;
using GameBot.Service.Services.Notifications;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Acts on a queue's failure policy when a cycle completes (feature 087, issue #181).
/// <para>
/// Feature 086 published <c>consecutiveFailedCycles</c> and deliberately left it inert. This is the
/// component that reads it and does something — and it is deliberately a <b>separate</b> component
/// from <see cref="QueueCycleLedger"/>, not a change to it. The ledger keeps its property of being
/// a pure observer whose every mutator is void and non-throwing and which no scheduling decision
/// consults from within; this evaluator reads the snapshot the ledger already publishes and may act
/// on it. Keeping the two apart is what makes acting on a failing run safe.
/// </para>
/// <para>
/// <b>Never throws.</b> It is called from the run loop that drives production farms; a fault in
/// escalation must not become a fault in execution.
/// </para>
/// </summary>
internal sealed class QueueFailurePolicyEvaluator {
  private readonly IFailureNotifier _notifier;
  private readonly ISequenceRepository _sequences;
  private readonly TimeProvider _timeProvider;

  public QueueFailurePolicyEvaluator(
    IFailureNotifier notifier,
    ISequenceRepository sequences,
    TimeProvider timeProvider) {
    _notifier = notifier;
    _sequences = sequences;
    _timeProvider = timeProvider;
  }

  /// <summary>
  /// Evaluates the policy against the cycle that has just completed, and performs the configured
  /// action when it trips.
  /// <para>
  /// Called from the run loop immediately after the ledger seals a cycle. A queue with no policy
  /// configured returns before touching the ledger at all, so it pays nothing (FR-004).
  /// </para>
  /// </summary>
  public void OnCycleCompleted(ExecutionQueue queue, QueueRunHandle handle) {
    try {
      if (queue.FailurePolicy is not { } policy) return;

      var health = handle.Cycles.SnapshotHealth();
      if (health.LastCycleSucceeded is not { } succeeded) return;

      if (succeeded) {
        // A clean cycle re-arms the policy, so a later episode alerts again (FR-008).
        handle.ClearPolicyTripped();
        return;
      }

      // `>=` rather than `==`: a threshold crossed by more than one would otherwise be missed
      // forever. The trip flag is what bounds this to one action per episode (FR-009).
      if (health.ConsecutiveFailedCycles < policy.ConsecutiveFailedCycles) return;
      // Feature 106: one atomic check and mark. The liveness watch and the run loop can call this
      // method at the same time, and only one of them may act.
      if (!handle.TryMarkPolicyTripped()) return;

      Act(queue, handle, policy, health.ConsecutiveFailedCycles, health.CyclesCompleted);
    }
    catch (Exception) {
      // Escalation must never take down the run it is escalating about.
    }
  }

  private void Act(
    ExecutionQueue queue,
    QueueRunHandle handle,
    QueueFailurePolicy policy,
    int consecutiveFailures,
    int cyclesCompleted) {
    if (policy.Notifies) {
      StartNotification(queue, handle, policy, consecutiveFailures, cyclesCompleted);
    }

    if (policy.Action == QueueFailureAction.Pause) {
      var reason = string.Format(
        CultureInfo.InvariantCulture,
        "failure policy: {0} consecutive failed cycles",
        consecutiveFailures);
      handle.EnterPolicyPause(reason, _timeProvider.GetLocalNow());
      return;
    }

    if (policy.Stops) {
      // Order matters twice over: the marker must be set before the cancellation so the terminating
      // handler can attribute the stop, and the notification must already be in flight (above) so a
      // notifyAndStop alert is not cancelled by the stop it is announcing.
      handle.MarkStopRequestedByPolicy();
      try { handle.Cts.Cancel(); }
      catch (ObjectDisposedException) { /* run already ending */ }
    }
  }

  /// <summary>
  /// Builds the event and starts delivery <b>off</b> the calling thread, with a token that is not
  /// the run's. The run loop never awaits this: a black-hole receiver must cost a cycle nothing
  /// (SC-005), and a <c>notifyAndStop</c> alert must outlive the stop it announces.
  /// </summary>
  private void StartNotification(
    ExecutionQueue queue,
    QueueRunHandle handle,
    QueueFailurePolicy policy,
    int consecutiveFailures,
    int cyclesCompleted) {
    var failure = DescribeFailedEntries(handle);
    var evt = new FailureNotificationEvent {
      EventType = FailureNotificationEvent.QueueFailurePolicyEvent,
      RaisedAt = _timeProvider.GetLocalNow(),
      QueueId = queue.Id,
      QueueName = queue.Name,
      EmulatorSerial = queue.EmulatorSerial,
      ConsecutiveFailedCycles = consecutiveFailures,
      CyclesCompleted = cyclesCompleted,
      FailedEntryIndex = failure.Index,
      FailedSequenceId = failure.SequenceId,
      FailedEntryCount = failure.Count,
      Action = QueueFailurePolicyMapping.ActionToWire(policy.Action),
      Message = string.Format(
        CultureInfo.InvariantCulture,
        "Queue '{0}' has failed {1} consecutive cycles; action: {2}.",
        queue.Name,
        consecutiveFailures,
        QueueFailurePolicyMapping.ActionToWire(policy.Action))
    };

    _ = Task.Run(async () => {
      try {
        // Resolved at send time so the name can never be stale — the ledger deliberately stores
        // ids only and never touches a repository (feature 086's rule, preserved here).
        evt.FailedSequenceName = await ResolveSequenceNameAsync(failure.SequenceId).ConfigureAwait(false);
        var result = await _notifier.NotifyAsync(evt, policy.NotifyUrl, CancellationToken.None).ConfigureAwait(false);
        handle.RecordNotification(result.CompletedAt, result.Succeeded, result.Error);
      }
      catch (Exception ex) {
        handle.RecordNotification(_timeProvider.GetLocalNow(), false, ex.GetType().Name);
      }
    });
  }

  private async Task<string?> ResolveSequenceNameAsync(string? sequenceId) {
    if (string.IsNullOrEmpty(sequenceId)) return null;
    try {
      var all = await _sequences.ListAsync().ConfigureAwait(false);
      return all.FirstOrDefault(s => string.Equals(s.Id, sequenceId, StringComparison.Ordinal))?.Name;
    }
    catch (Exception) {
      return null;
    }
  }

  /// <summary>
  /// The first failed entry of the most recent completed cycle, plus how many failed in total.
  /// The first is what an operator looks at; the count tells one flaky task from total breakage.
  /// </summary>
  private static (int? Index, string? SequenceId, int Count) DescribeFailedEntries(QueueRunHandle handle) {
    var recent = handle.Cycles.SnapshotCycles(1);
    if (recent.Count == 0) return (null, null, 0);

    var entries = recent[0].Entries;
    int? firstIndex = null;
    string? firstSequence = null;
    var failed = 0;
    for (var i = 0; i < entries.Count; i++) {
      if (entries[i].Succeeded) continue;
      failed++;
      if (firstIndex is null) {
        firstIndex = i;
        firstSequence = entries[i].SequenceId;
      }
    }
    return (firstIndex, firstSequence, failed);
  }
}
