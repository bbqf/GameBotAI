using System;
using System.Collections.Generic;
using GameBot.Domain.Notifications;

namespace GameBot.Service.Services.Notifications;

/// <summary>The result of a queue entry run, as the notification code sees it (feature 120).</summary>
internal enum NotificationRunStatus {
  Success,
  Failure,
  Cancelled
}

/// <summary>The status word in a message. <c>Recovered</c> follows an open failure streak.</summary>
internal enum NotificationMessageStatus {
  Success,
  Failure,
  Cancelled,
  Recovered
}

/// <summary>One field of a target type. The UI builds its form from the field list.</summary>
internal sealed record NotificationField(string Key, string Label, bool Secret, bool Required);

/// <summary>
/// The result of one send to one target. <see cref="Reason"/> is safe text and never has a secret.
/// </summary>
internal sealed record NotificationSendResult(bool Succeeded, string? Reason) {
  public static NotificationSendResult Ok() => new(true, null);

  public static NotificationSendResult Failed(string reason) => new(false, reason);
}

/// <summary>
/// One finished queue entry run. The engine writes it to the dispatcher (feature 120). It has no
/// names: the worker reads the names when it sends.
/// </summary>
internal sealed record QueueNotificationJob(
  string QueueId,
  string SequenceId,
  NotificationRunStatus Status,
  DateTimeOffset RaisedAt);

/// <summary>The kind of a queue alert (feature 121).</summary>
internal enum QueueAlertKind {
  /// <summary>The device stayed not live for longer than the alert time.</summary>
  NotLive,

  /// <summary>The device is live again after a not-live alert.</summary>
  LiveAgain,

  /// <summary>The last allowed recovery attempt failed.</summary>
  RecoveryFailed
}

/// <summary>
/// An alert about the device of a queue (feature 121). The worker sends it to all enabled targets. It
/// ignores the notification level of the queue and the failure streaks. It is never dropped.
/// </summary>
/// <param name="QueueId">The queue. The worker reads the name when it sends.</param>
/// <param name="Kind">The kind of the alert.</param>
/// <param name="Reason">The liveness reason for <see cref="QueueAlertKind.NotLive"/>. Null for the other kinds.</param>
/// <param name="RaisedAt">The local time of the claim.</param>
/// <param name="Attempts">The count of finished recovery attempts. Used by <see cref="QueueAlertKind.RecoveryFailed"/>.</param>
/// <param name="OnCompleted">
/// Called one time when the sends end and at least one target existed: the time, the result (true when at
/// least one target accepted the message) and a safe error text. It is not called when no target exists.
/// </param>
internal sealed record QueueAlert(
  string QueueId,
  QueueAlertKind Kind,
  string? Reason,
  DateTimeOffset RaisedAt,
  int Attempts = 0,
  Action<DateTimeOffset, bool, string?>? OnCompleted = null);

/// <summary>
/// An item in the one channel of the dispatcher. It is a run job, an alert, or a reset control
/// message. A reset message and an alert are never dropped.
/// </summary>
internal sealed class NotificationWork {
  private NotificationWork(QueueNotificationJob? job, string? resetQueueId, QueueAlert? alert) {
    Job = job;
    ResetQueueId = resetQueueId;
    Alert = alert;
  }

  public QueueNotificationJob? Job { get; }

  public string? ResetQueueId { get; }

  public QueueAlert? Alert { get; }

  public static NotificationWork ForJob(QueueNotificationJob job) => new(job, null, null);

  public static NotificationWork ForReset(string queueId) => new(null, queueId, null);

  public static NotificationWork ForAlert(QueueAlert alert) => new(null, null, alert);
}

/// <summary>
/// A channel is one target type, for example Telegram (feature 120). A new type needs one class and
/// one DI registration. <see cref="SendAsync"/> MUST NOT throw and MUST NOT put a secret in the reason.
/// </summary>
internal interface INotificationChannel {
  string Type { get; }

  string DisplayName { get; }

  IReadOnlyList<NotificationField> Fields { get; }

  /// <summary>Returns null when the target is valid, else a safe error text.</summary>
  string? Validate(NotificationTarget target);

  Task<NotificationSendResult> SendAsync(NotificationTarget target, string text, CancellationToken ct);
}

/// <summary>
/// The entry point for the queue engine and the queue endpoints (feature 120). Both methods write to
/// one channel that one worker reads, and both return at once.
/// </summary>
internal interface INotificationDispatcher {
  /// <summary>Hands one finished run to the worker. Never blocks and never throws.</summary>
  void Enqueue(QueueNotificationJob job);

  /// <summary>
  /// Closes all open failure streaks of a queue, with no message. The level route calls it after a
  /// save of level None. The delete route calls it after a queue delete. Never dropped.
  /// </summary>
  void ResetStreaks(string queueId);

  /// <summary>
  /// Hands one device alert to the worker (feature 121). The item is never dropped, is not part of
  /// <c>MaxQueuedJobs</c>, never blocks, and never throws.
  /// </summary>
  void SendAlert(QueueAlert alert);
}

/// <summary>Limits of the dispatcher and the send. The defaults are the values of the design.</summary>
internal sealed class NotificationDispatchLimits {
  /// <summary>Above this count of run jobs that wait, <c>Enqueue</c> drops the new job.</summary>
  public int MaxQueuedJobs { get; init; } = 256;

  /// <summary>Above this count of send tasks that run, the worker drops the message.</summary>
  public int MaxParallelSends { get; init; } = 64;

  /// <summary>One total limit for the send of one message to all targets.</summary>
  public TimeSpan SendTimeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>Options of the notification feature. Section <c>Service:Notifications</c>.</summary>
internal sealed class NotificationOptions {
  public const string SectionName = "Service:Notifications";

  /// <summary>Base URL of the Telegram Bot API. Tests point it to a fake handler.</summary>
  public string TelegramBaseUrl { get; set; } = "https://api.telegram.org";

  /// <summary>Time limit of one Telegram attempt.</summary>
  public TimeSpan TelegramAttemptTimeout { get; set; } = TimeSpan.FromSeconds(10);

  /// <summary>Pause between the two Telegram attempts.</summary>
  public TimeSpan TelegramRetryPause { get; set; } = TimeSpan.FromSeconds(1);
}
