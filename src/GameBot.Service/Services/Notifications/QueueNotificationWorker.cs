using GameBot.Domain.Commands;
using GameBot.Domain.Notifications;
using GameBot.Domain.Queues;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.Notifications;

/// <summary>
/// The one reader of the work channel and the only owner of the failure streak state (feature 120,
/// FR-023). It reads the level of the queue when it handles a job, applies the level and streak rules
/// of research R-003, and starts the sends for the message. It never waits for a send, so a stuck
/// target cannot delay the next job. Sends to one target for one queue and sequence pair run in the
/// order of the jobs (FR-024).
/// </summary>
internal sealed partial class QueueNotificationWorker : BackgroundService {
  private readonly QueueNotificationDispatcher _dispatcher;
  private readonly IQueueRepository _queues;
  private readonly ISequenceRepository _sequences;
  private readonly INotificationTargetStore _targets;
  private readonly Dictionary<string, INotificationChannel> _channels;
  private readonly NotificationDispatchLimits _limits;
  private readonly ILogger<QueueNotificationWorker> _logger;
  private readonly NotificationStreakState _streaks = new();
  private readonly Dictionary<string, SendChain> _chains = new(StringComparer.Ordinal);
  private int _activeSends;

  public QueueNotificationWorker(
    QueueNotificationDispatcher dispatcher,
    IQueueRepository queues,
    ISequenceRepository sequences,
    INotificationTargetStore targets,
    IEnumerable<INotificationChannel> channels,
    ILogger<QueueNotificationWorker> logger,
    NotificationDispatchLimits? limits = null) {
    _dispatcher = dispatcher;
    _queues = queues;
    _sequences = sequences;
    _targets = targets;
    _channels = channels.ToDictionary(c => c.Type, StringComparer.OrdinalIgnoreCase);
    _limits = limits ?? new NotificationDispatchLimits();
    _logger = logger;
  }

  /// <summary>The streak state. Tests read it. Only the worker writes it.</summary>
  internal NotificationStreakState Streaks => _streaks;

  /// <summary>The count of send tasks that run now.</summary>
  internal int ActiveSends => Volatile.Read(ref _activeSends);

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    try {
      await foreach (var work in _dispatcher.Reader.ReadAllAsync(stoppingToken).ConfigureAwait(false)) {
        await HandleAsync(work).ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
      // The host stops. Jobs that wait are lost. This is accepted.
    }
  }

  /// <summary>Handles one work item. Never throws.</summary>
  internal async Task HandleAsync(NotificationWork work) {
    try {
      if (work.ResetQueueId is { } resetId) {
        var removed = _streaks.RemoveQueue(resetId);
        Log.StreaksReset(_logger, resetId, removed);
        return;
      }

      if (work.Alert is { } alert) {
        await HandleAlertAsync(alert).ConfigureAwait(false);
        return;
      }

      if (work.Job is { } job) {
        try {
          await HandleJobAsync(job).ConfigureAwait(false);
        }
        finally {
          _dispatcher.MarkJobHandled();
        }
      }
    }
    catch (Exception ex) {
      Log.HandleFaulted(_logger, ex.GetType().Name);
    }
  }

  private async Task HandleJobAsync(QueueNotificationJob job) {
    ExecutionQueue? queue;
    try {
      queue = await _queues.GetAsync(job.QueueId).ConfigureAwait(false);
    }
    catch (Exception ex) {
      Log.QueueReadFailed(_logger, job.QueueId, ex.GetType().Name);
      return;
    }

    // A job for a deleted queue is dropped. A job for a level None queue changes no state.
    if (queue is null || queue.NotificationLevel == NotificationLevel.None) return;

    // Feature 122: one read of the sequence gives the name and the exclude option. If the read fails,
    // the option is treated as off.
    var (sequenceName, excludeSuccess) = await ReadSequenceAsync(job.SequenceId).ConfigureAwait(false);
    var message = Decide(queue.NotificationLevel, job, excludeSuccess);
    if (message is null) return;

    var text = NotificationMessageFormatter.Format(queue.Name, queue.Id, sequenceName, job.SequenceId, message.Value);
    StartSend(text, job);
  }

  // Feature 121: a device alert goes to all enabled targets. It ignores the level of the queue and the
  // streak state, and it is never dropped by the send cap. The worker does not wait for the sends.
  private async Task HandleAlertAsync(QueueAlert alert) {
    string? queueName = null;
    try {
      queueName = (await _queues.GetAsync(alert.QueueId).ConfigureAwait(false))?.Name;
    }
    catch (Exception ex) {
      Log.QueueReadFailed(_logger, alert.QueueId, ex.GetType().Name);
    }

    var text = NotificationMessageFormatter.FormatAlert(queueName, alert.QueueId, alert.Kind, alert.Reason, alert.Attempts);
    List<NotificationTarget> targets;
    try {
      targets = _targets.List().Where(t => t.Enabled).ToList();
    }
    catch (Exception ex) {
      Log.SendFaulted(_logger, ex.GetType().Name);
      return;
    }

    if (targets.Count == 0) {
      Log.AlertNoTarget(_logger, alert.QueueId, alert.Kind);
      return;
    }

    var sends = targets.Select(target => ChainSend(target, text, alert.QueueId, "alert")).ToList();
    _ = Task.Run(async () => {
      try {
        var results = await Task.WhenAll(sends).ConfigureAwait(false);
        var ok = results.Any(r => r.Succeeded);
        var error = ok ? null : results.Select(r => r.Reason).FirstOrDefault(r => !string.IsNullOrEmpty(r)) ?? "unknown";
        alert.OnCompleted?.Invoke(DateTimeOffset.Now, ok, error);
      }
      catch (Exception ex) {
        Log.SendFaulted(_logger, ex.GetType().Name);
      }
    });
  }

  // The state table of research R-003, for levels Failure and Success+Failure.
  // Feature 122: a sequence with the exclude option gets no plain "success" message. A "recovered"
  // message for an open streak is still sent.
  private NotificationMessageStatus? Decide(NotificationLevel level, QueueNotificationJob job, bool excludeSuccess) {
    switch (job.Status) {
      case NotificationRunStatus.Cancelled:
        // A cancelled run sends a message and changes no streak.
        return NotificationMessageStatus.Cancelled;

      case NotificationRunStatus.Failure:
        if (_streaks.IsOpen(job.QueueId, job.SequenceId)) return null;
        _streaks.Open(job.QueueId, job.SequenceId);
        return NotificationMessageStatus.Failure;

      default:
        if (_streaks.IsOpen(job.QueueId, job.SequenceId)) {
          _streaks.Close(job.QueueId, job.SequenceId);
          return NotificationMessageStatus.Recovered;
        }

        return level == NotificationLevel.SuccessAndFailure && !excludeSuccess ? NotificationMessageStatus.Success : null;
    }
  }

  private async Task<(string? Name, bool ExcludeSuccess)> ReadSequenceAsync(string sequenceId) {
    try {
      var sequence = await _sequences.GetAsync(sequenceId).ConfigureAwait(false);
      return (sequence?.Name, sequence?.ExcludeFromSuccessNotifications ?? false);
    }
    catch (Exception ex) {
      Log.SequenceReadFailed(_logger, sequenceId, ex.GetType().Name);
      return (null, false);
    }
  }

  // Starts one send task and does not wait for it. The task never touches the streak state.
  private void StartSend(string text, QueueNotificationJob job) {
    if (Interlocked.Increment(ref _activeSends) > _limits.MaxParallelSends) {
      Interlocked.Decrement(ref _activeSends);
      Log.SendDropped(_logger, job.QueueId, job.SequenceId, _limits.MaxParallelSends);
      return;
    }

    List<NotificationTarget> targets;
    try {
      targets = _targets.List().Where(t => t.Enabled).ToList();
    }
    catch (Exception ex) {
      Log.SendFaulted(_logger, ex.GetType().Name);
      Interlocked.Decrement(ref _activeSends);
      return;
    }

    if (targets.Count == 0) {
      Interlocked.Decrement(ref _activeSends);
      return;
    }

    // The worker thread chains here, in the order of the jobs. This gives the send order for each
    // target, queue and sequence (FR-024). Different targets and different pairs run in parallel.
    var sends = targets.Select(target => ChainSend(target, text, job.QueueId, job.SequenceId)).ToList();
    _ = Task.Run(async () => {
      try {
        await Task.WhenAll(sends).ConfigureAwait(false);
      }
      catch (Exception ex) {
        Log.SendFaulted(_logger, ex.GetType().Name);
      }
      finally {
        Interlocked.Decrement(ref _activeSends);
      }
    });
  }

  // A send waits for the earlier send of the same target, queue and sequence. That earlier send ends
  // at its own time limit, so a stuck target delays only later messages of the same pair.
  private Task<NotificationSendResult> ChainSend(NotificationTarget target, string text, string queueId, string sequenceKey) {
    var key = $"{target.Id}\u001f{queueId}\u001f{sequenceKey}";
    Task previous;
    Task<NotificationSendResult> current;
    lock (_chains) {
      if (_chains.TryGetValue(key, out var chain)) {
        previous = chain.Tail;
        chain.Pending++;
      }
      else {
        previous = Task.CompletedTask;
        chain = new SendChain { Pending = 1 };
        _chains[key] = chain;
      }

      current = Task.Run(() => RunChainedAsync(previous, key, target, text));
      chain.Tail = current;
    }

    return current;
  }

  private async Task<NotificationSendResult> RunChainedAsync(Task previous, string key, NotificationTarget target, string text) {
    try {
      try {
        await previous.ConfigureAwait(false);
      }
      catch (Exception) {
        // The earlier send handles its own errors. Nothing more to do here.
      }

      // Each message has one time limit. It starts when the send starts.
      using var limit = new CancellationTokenSource(_limits.SendTimeout);
      return await SendOneAsync(target, text, limit.Token).ConfigureAwait(false);
    }
    finally {
      lock (_chains) {
        if (_chains.TryGetValue(key, out var chain) && --chain.Pending == 0) _chains.Remove(key);
      }
    }
  }

  private sealed class SendChain {
    public Task Tail { get; set; } = Task.CompletedTask;

    public int Pending { get; set; }
  }

  private async Task<NotificationSendResult> SendOneAsync(NotificationTarget target, string text, CancellationToken limit) {
    if (!_channels.TryGetValue(target.Type, out var channel)) {
      Log.UnknownType(_logger, target.Id, target.Type);
      return NotificationSendResult.Failed("unknown target type");
    }

    try {
      var send = channel.SendAsync(target, text, limit);
      // WaitAsync ends at the limit also when the channel ignores the token.
      var result = await send.WaitAsync(_limits.SendTimeout, limit).ConfigureAwait(false);
      if (!result.Succeeded) {
        Log.TargetFailed(_logger, target.Id, target.Name, result.Reason ?? "unknown");
      }
      return result;
    }
    catch (Exception ex) when (ex is TimeoutException || (ex is OperationCanceledException && limit.IsCancellationRequested)) {
      // The limit source cancels its token at the same time limit. No manual cancel is necessary.
      Log.TargetTimedOut(_logger, target.Id, target.Name);
      return NotificationSendResult.Failed("time limit");
    }
    catch (Exception ex) {
      Log.TargetFaulted(_logger, target.Id, target.Name, ex.GetType().Name);
      return NotificationSendResult.Failed(ex.GetType().Name);
    }
  }

  private static partial class Log {
    [LoggerMessage(EventId = 12030, Level = LogLevel.Debug, Message = "Streaks of queue {QueueId} reset. Removed keys: {Removed}.")]
    public static partial void StreaksReset(ILogger logger, string queueId, int removed);

    [LoggerMessage(EventId = 12031, Level = LogLevel.Warning, Message = "The notification worker could not handle one item. Error type: {ErrorType}.")]
    public static partial void HandleFaulted(ILogger logger, string errorType);

    [LoggerMessage(EventId = 12032, Level = LogLevel.Warning, Message = "The queue {QueueId} could not be read for a notification. Error type: {ErrorType}.")]
    public static partial void QueueReadFailed(ILogger logger, string queueId, string errorType);

    [LoggerMessage(EventId = 12033, Level = LogLevel.Warning, Message = "The sequence {SequenceId} could not be read for a notification. The ID is used as the name. The option to exclude success notifications is treated as off. Error type: {ErrorType}.")]
    public static partial void SequenceReadFailed(ILogger logger, string sequenceId, string errorType);

    [LoggerMessage(EventId = 12034, Level = LogLevel.Warning, Message = "Notification dropped: {Cap} sends already run. Queue {QueueId}, sequence {SequenceId}.")]
    public static partial void SendDropped(ILogger logger, string queueId, string sequenceId, int cap);

    [LoggerMessage(EventId = 12035, Level = LogLevel.Warning, Message = "A notification send task faulted. Error type: {ErrorType}.")]
    public static partial void SendFaulted(ILogger logger, string errorType);

    [LoggerMessage(EventId = 12036, Level = LogLevel.Warning, Message = "Target {TargetId} has the unknown type {TargetType}. The target is skipped.")]
    public static partial void UnknownType(ILogger logger, string targetId, string targetType);

    [LoggerMessage(EventId = 12037, Level = LogLevel.Warning, Message = "Notification to target {TargetId} ({TargetName}) failed: {Reason}")]
    public static partial void TargetFailed(ILogger logger, string targetId, string targetName, string reason);

    [LoggerMessage(EventId = 12038, Level = LogLevel.Warning, Message = "Notification to target {TargetId} ({TargetName}) did not end within the time limit. The message is dropped for this target.")]
    public static partial void TargetTimedOut(ILogger logger, string targetId, string targetName);

    [LoggerMessage(EventId = 12040, Level = LogLevel.Information, Message = "No notification target is available for the {Kind} alert of queue {QueueId}. The alert is not sent.")]
    public static partial void AlertNoTarget(ILogger logger, string queueId, QueueAlertKind kind);

    [LoggerMessage(EventId = 12039, Level = LogLevel.Warning, Message = "Notification to target {TargetId} ({TargetName}) faulted. Error type: {ErrorType}.")]
    public static partial void TargetFaulted(ILogger logger, string targetId, string targetName, string errorType);
  }
}
