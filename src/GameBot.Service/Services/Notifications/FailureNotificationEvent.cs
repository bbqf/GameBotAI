using System;

namespace GameBot.Service.Services.Notifications {
  /// <summary>
  /// The payload delivered to a configured notification destination (feature 087, issue #181).
  /// <para>
  /// <b>This is a published contract.</b> A receiver is written against it, so field names and
  /// meanings are stable; a breaking change increments <see cref="SchemaVersion"/> rather than
  /// altering a field's meaning in place. The canonical definition lives in
  /// <c>specs/087-queue-failure-policy/contracts/notification-payload.md</c>.
  /// </para>
  /// </summary>
  internal sealed class FailureNotificationEvent {
    /// <summary>Current schema version of this contract.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary><see cref="EventType"/> value for a tripped queue failure policy.</summary>
    public const string QueueFailurePolicyEvent = "queue.failure-policy";

    /// <summary><see cref="EventType"/> value for an alert raised by a notify step in a sequence.</summary>
    public const string SequenceNotifyEvent = "sequence.notify";

    /// <summary>Contract version. Increments only on a breaking change.</summary>
    public int SchemaVersion { get; set; } = CurrentSchemaVersion;

    /// <summary>
    /// <see cref="QueueFailurePolicyEvent"/> or <see cref="SequenceNotifyEvent"/>. A receiver keys
    /// off this to tell an automatic escalation from one an author wrote.
    /// </summary>
    public string EventType { get; set; } = QueueFailurePolicyEvent;

    /// <summary>Local-clock instant the event was raised, matching the execution log's clock.</summary>
    public DateTimeOffset RaisedAt { get; set; }

    /// <summary>Stable queue id; null for a sequence alert raised outside any queue.</summary>
    public string? QueueId { get; set; }

    /// <summary>Queue display name at the moment of the event; null outside a queue.</summary>
    public string? QueueName { get; set; }

    /// <summary>The device the queue drives — usually the fastest way to identify which farm broke.</summary>
    public string? EmulatorSerial { get; set; }

    /// <summary>The consecutive-failed-cycle count that tripped the policy; 0 for a sequence alert.</summary>
    public int ConsecutiveFailedCycles { get; set; }

    /// <summary>
    /// Cycles completed in the current run — distinguishes "broke immediately" from "ran 400 cycles
    /// and then broke", which are very different diagnoses.
    /// </summary>
    public int CyclesCompleted { get; set; }

    /// <summary>Roster position of the first failed entry in the tripping cycle; null for a sequence alert.</summary>
    public int? FailedEntryIndex { get; set; }

    /// <summary>First failing sequence, or the sequence that raised the alert.</summary>
    public string? FailedSequenceId { get; set; }

    /// <summary>Resolved at send time so it is never stale; null if the sequence no longer exists.</summary>
    public string? FailedSequenceName { get; set; }

    /// <summary>
    /// How many entries failed in the tripping cycle — tells "one flaky task" from "everything is
    /// broken". Zero for a sequence alert.
    /// </summary>
    public int FailedEntryCount { get; set; }

    /// <summary>The action the policy took; null for a sequence alert.</summary>
    public string? Action { get; set; }

    /// <summary>
    /// Human-readable summary. Composed by the service for a policy trip; written by the sequence
    /// author for a notify step.
    /// </summary>
    public string Message { get; set; } = string.Empty;
  }
}
