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

/// <summary>
/// An item in the one channel of the dispatcher. It is a run job or a reset control message. A reset
/// message is never dropped.
/// </summary>
internal sealed class NotificationWork {
  private NotificationWork(QueueNotificationJob? job, string? resetQueueId) {
    Job = job;
    ResetQueueId = resetQueueId;
  }

  public QueueNotificationJob? Job { get; }

  public string? ResetQueueId { get; }

  public static NotificationWork ForJob(QueueNotificationJob job) => new(job, null);

  public static NotificationWork ForReset(string queueId) => new(null, queueId);
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
