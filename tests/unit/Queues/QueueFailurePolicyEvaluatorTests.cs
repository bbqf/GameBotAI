using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Queues;
using GameBot.Service.Services.Notifications;
using GameBot.Service.Services.QueueExecution;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// The decision table of the failure-policy evaluator (feature 087, issue #181).
/// <para>
/// The condition under test is the one the issue was filed about: a queue whose cycles keep failing
/// must escalate exactly once per episode — not never (the 44-hour outage) and not every cycle (an
/// alert storm that buries the signal it was raised to give).
/// </para>
/// </summary>
public class QueueFailurePolicyEvaluatorTests {
  private static readonly DateTimeOffset T0 =
    new DateTimeOffset(2026, 9, 14, 10, 0, 0, TimeSpan.FromHours(2));

  /// <summary>Records every event it is handed and reports whatever outcome the test asked for.</summary>
  private sealed class RecordingNotifier : IFailureNotifier {
    public List<FailureNotificationEvent> Sent { get; } = new();
    public List<string?> Urls { get; } = new();
    public bool Succeeds { get; set; } = true;
    public string? Error { get; set; }

    public Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) {
      lock (Sent) {
        Sent.Add(evt);
        Urls.Add(overrideUrl);
      }
      return Task.FromResult(new FailureNotificationResult(Succeeds, T0, Succeeds ? null : Error));
    }
  }

  private sealed class StubSequenceRepository : ISequenceRepository {
    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
      Task.FromResult<IReadOnlyList<CommandSequence>>(new ReadOnlyCollection<CommandSequence>(new List<CommandSequence> {
        new CommandSequence { Id = "seq-0", Name = "PNS.CollectResources" },
        new CommandSequence { Id = "seq-1", Name = "PNS.DonateAllianceTech" }
      }));

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(null);
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static ExecutionQueue QueueWith(QueueFailureAction action, int threshold, string? url = "http://localhost:9099/alerts") =>
    new ExecutionQueue {
      Id = "q1",
      Name = "PNS Daily 5558",
      EmulatorSerial = "emulator-5558",
      FailurePolicy = new QueueFailurePolicy {
        ConsecutiveFailedCycles = threshold,
        Action = action,
        NotifyUrl = url
      }
    };

  private static QueueRunHandle NewHandle() =>
    new QueueRunHandle { QueueId = "q1", Cts = new CancellationTokenSource() };

  private static (QueueFailurePolicyEvaluator Evaluator, RecordingNotifier Notifier) NewEvaluator() {
    var notifier = new RecordingNotifier();
    var evaluator = new QueueFailurePolicyEvaluator(
      notifier, new StubSequenceRepository(), new FakeTimeProvider(T0));
    return (evaluator, notifier);
  }

  /// <summary>Completes one cycle on the handle's ledger with the given per-entry outcomes.</summary>
  private static void RunCycle(QueueRunHandle handle, params bool[] outcomes) {
    handle.Cycles.EnsureOpen(T0);
    for (var i = 0; i < outcomes.Length; i++) handle.Cycles.RecordEntry($"seq-{i}", outcomes[i]);
    handle.Cycles.CompleteOpen(T0);
  }

  /// <summary>Waits for the fire-and-forget delivery the evaluator starts off the calling thread.</summary>
  private static async Task<List<FailureNotificationEvent>> SettledAsync(RecordingNotifier notifier, int expected) {
    for (var i = 0; i < 100; i++) {
      lock (notifier.Sent) {
        if (notifier.Sent.Count >= expected) return notifier.Sent.ToList();
      }
      await Task.Delay(10).ConfigureAwait(false);
    }
    lock (notifier.Sent) { return notifier.Sent.ToList(); }
  }

  [Fact]
  public async Task BelowThreshold_DoesNotTripOrNotify() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 3);
    var handle = NewHandle();

    for (var i = 0; i < 2; i++) {
      RunCycle(handle, false);
      evaluator.OnCycleCompleted(queue, handle);
    }

    await Task.Delay(50).ConfigureAwait(false);
    handle.PolicyTripped.Should().BeFalse();
    notifier.Sent.Should().BeEmpty();
  }

  [Fact]
  public async Task AtThreshold_TripsAndNotifiesOnce() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 3);
    var handle = NewHandle();

    for (var i = 0; i < 3; i++) {
      RunCycle(handle, false);
      evaluator.OnCycleCompleted(queue, handle);
    }

    var sent = await SettledAsync(notifier, 1).ConfigureAwait(false);
    handle.PolicyTripped.Should().BeTrue();
    sent.Should().HaveCount(1);
    sent[0].ConsecutiveFailedCycles.Should().Be(3);
    sent[0].QueueId.Should().Be("q1");
    sent[0].QueueName.Should().Be("PNS Daily 5558");
    sent[0].EmulatorSerial.Should().Be("emulator-5558");
    sent[0].EventType.Should().Be(FailureNotificationEvent.QueueFailurePolicyEvent);
    sent[0].SchemaVersion.Should().Be(1);
  }

  /// <summary>
  /// The alert-storm guard (FR-009). This is the behaviour that distinguishes a usable alert from
  /// an overnight flood of them, and the issue calls it out explicitly.
  /// </summary>
  [Fact]
  public async Task PastThreshold_DoesNotNotifyAgainWhileStillFailing() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 2);
    var handle = NewHandle();

    for (var i = 0; i < 10; i++) {
      RunCycle(handle, false);
      evaluator.OnCycleCompleted(queue, handle);
    }

    var sent = await SettledAsync(notifier, 1).ConfigureAwait(false);
    sent.Should().HaveCount(1, "ten consecutive failed cycles are one episode, not ten");
  }

  [Fact]
  public async Task SuccessfulCycle_ResetsAndReArmsForTheNextEpisode() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 2);
    var handle = NewHandle();

    // Episode one.
    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);
    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);
    await SettledAsync(notifier, 1).ConfigureAwait(false);
    handle.PolicyTripped.Should().BeTrue();

    // Recovery re-arms.
    RunCycle(handle, true);
    evaluator.OnCycleCompleted(queue, handle);
    handle.PolicyTripped.Should().BeFalse();

    // Episode two notifies again.
    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);
    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);

    var sent = await SettledAsync(notifier, 2).ConfigureAwait(false);
    sent.Should().HaveCount(2);
  }

  [Fact]
  public async Task NoPolicyConfigured_DoesNothingAtAll() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = new ExecutionQueue { Id = "q1", Name = "unpoliced", EmulatorSerial = "emulator-5558" };
    var handle = NewHandle();

    for (var i = 0; i < 20; i++) {
      RunCycle(handle, false);
      evaluator.OnCycleCompleted(queue, handle);
    }

    await Task.Delay(50).ConfigureAwait(false);
    handle.PolicyTripped.Should().BeFalse();
    notifier.Sent.Should().BeEmpty();
    handle.Cts.IsCancellationRequested.Should().BeFalse();
  }

  [Fact]
  public async Task NotifiesWithFailingEntryAndCount() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 1);
    var handle = NewHandle();

    // Entry 0 succeeds, 1 and 2 fail: the first failure is what an operator looks at, the count
    // tells one flaky task from total breakage.
    RunCycle(handle, true, false, false);
    evaluator.OnCycleCompleted(queue, handle);

    var sent = await SettledAsync(notifier, 1).ConfigureAwait(false);
    sent[0].FailedEntryIndex.Should().Be(1);
    sent[0].FailedSequenceId.Should().Be("seq-1");
    sent[0].FailedSequenceName.Should().Be("PNS.DonateAllianceTech", "the name is resolved at send time, never stored");
    sent[0].FailedEntryCount.Should().Be(2);
  }

  [Fact]
  public void StopAction_CancelsTheRunAndMarksTheReason() {
    var (evaluator, _) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Stop, threshold: 1);
    var handle = NewHandle();

    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);

    handle.Cts.IsCancellationRequested.Should().BeTrue();
    handle.StopRequestedByPolicy.Should().BeTrue(
      "otherwise the terminating handler reports a policy stop as an operator stop");
  }

  [Fact]
  public async Task StopAction_DoesNotNotify() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Stop, threshold: 1);
    var handle = NewHandle();

    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);

    await Task.Delay(50).ConfigureAwait(false);
    notifier.Sent.Should().BeEmpty();
  }

  [Fact]
  public async Task NotifyAndStop_NotifiesAndCancels() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.NotifyAndStop, threshold: 1);
    var handle = NewHandle();

    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);

    var sent = await SettledAsync(notifier, 1).ConfigureAwait(false);
    sent.Should().HaveCount(1);
    sent[0].Action.Should().Be("notifyAndStop");
    handle.Cts.IsCancellationRequested.Should().BeTrue();
    handle.StopRequestedByPolicy.Should().BeTrue();
  }

  [Fact]
  public void PauseAction_ParksTheRunWithAReasonAndDoesNotCancel() {
    var (evaluator, _) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Pause, threshold: 4);
    var handle = NewHandle();

    for (var i = 0; i < 4; i++) {
      RunCycle(handle, false);
      evaluator.OnCycleCompleted(queue, handle);
    }

    handle.IsPolicyPaused.Should().BeTrue();
    handle.PolicyPausedAt.Should().Be(T0);
    handle.PauseReason.Should().Contain("4");
    handle.Cts.IsCancellationRequested.Should().BeFalse("pause parks the run, it does not end it");
  }

  [Fact]
  public async Task PolicyNotifyUrl_IsPassedToTheNotifier() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 1, url: "https://alerts.example/hook");
    var handle = NewHandle();

    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);

    await SettledAsync(notifier, 1).ConfigureAwait(false);
    notifier.Urls.Should().ContainSingle().Which.Should().Be("https://alerts.example/hook");
  }

  [Fact]
  public async Task DeliveryOutcome_IsRecordedOnTheHandle() {
    var (evaluator, notifier) = NewEvaluator();
    notifier.Succeeds = false;
    notifier.Error = "connection refused";
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 1);
    var handle = NewHandle();

    RunCycle(handle, false);
    evaluator.OnCycleCompleted(queue, handle);
    await SettledAsync(notifier, 1).ConfigureAwait(false);

    for (var i = 0; i < 100 && handle.LastNotification.At is null; i++) {
      await Task.Delay(10).ConfigureAwait(false);
    }

    handle.LastNotification.Succeeded.Should().BeFalse();
    handle.LastNotification.Error.Should().Be("connection refused");
  }

  /// <summary>
  /// A cycle that was interrupted rather than completed never reaches the ledger, so the policy
  /// must not see it. Guards against a stop or a lost connection tripping a policy on the way out.
  /// </summary>
  [Fact]
  public async Task NoCompletedCycle_IsNeitherSuccessNorFailure() {
    var (evaluator, notifier) = NewEvaluator();
    var queue = QueueWith(QueueFailureAction.NotifyAndStop, threshold: 1);
    var handle = NewHandle();

    handle.Cycles.EnsureOpen(T0);
    handle.Cycles.RecordEntry("seq-0", false);
    // No CompleteOpen: the cycle is abandoned.
    evaluator.OnCycleCompleted(queue, handle);

    await Task.Delay(50).ConfigureAwait(false);
    notifier.Sent.Should().BeEmpty();
    handle.Cts.IsCancellationRequested.Should().BeFalse();
  }

  /// <summary>
  /// The evaluator is called from the run loop that drives production farms. A fault in escalation
  /// must never become a fault in execution.
  /// </summary>
  [Fact]
  public void ThrowingNotifier_DoesNotEscape() {
    var throwing = new ThrowingNotifier();
    var evaluator = new QueueFailurePolicyEvaluator(
      throwing, new StubSequenceRepository(), new FakeTimeProvider(T0));
    var queue = QueueWith(QueueFailureAction.Notify, threshold: 1);
    var handle = NewHandle();

    RunCycle(handle, false);

    var act = () => evaluator.OnCycleCompleted(queue, handle);
    act.Should().NotThrow();
  }

  private sealed class ThrowingNotifier : IFailureNotifier {
    public Task<FailureNotificationResult> NotifyAsync(
      FailureNotificationEvent evt, string? overrideUrl, CancellationToken ct = default) =>
      throw new InvalidOperationException("receiver exploded");
  }
}
