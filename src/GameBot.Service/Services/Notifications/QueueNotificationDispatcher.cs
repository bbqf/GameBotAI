using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.Notifications;

/// <summary>
/// The write side of the one work channel (feature 120). The channel is unbounded and has one reader,
/// the <see cref="QueueNotificationWorker"/>. Both methods return at once, never block and never
/// throw, so a slow or dead target cannot slow a queue run (FR-010).
/// </summary>
internal sealed partial class QueueNotificationDispatcher : INotificationDispatcher {
  private readonly Channel<NotificationWork> _channel = Channel.CreateUnbounded<NotificationWork>(
    new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
  private readonly NotificationDispatchLimits _limits;
  private readonly ILogger<QueueNotificationDispatcher> _logger;
  private int _queuedRunJobs;

  public QueueNotificationDispatcher(ILogger<QueueNotificationDispatcher> logger, NotificationDispatchLimits? limits = null) {
    _logger = logger;
    _limits = limits ?? new NotificationDispatchLimits();
  }

  /// <summary>The read side. Only the worker reads it.</summary>
  public ChannelReader<NotificationWork> Reader => _channel.Reader;

  /// <summary>The count of run jobs that wait or run in the worker.</summary>
  public int QueuedRunJobs => Volatile.Read(ref _queuedRunJobs);

  public void Enqueue(QueueNotificationJob job) {
    try {
      ArgumentNullException.ThrowIfNull(job);
      if (Interlocked.Increment(ref _queuedRunJobs) > _limits.MaxQueuedJobs) {
        Interlocked.Decrement(ref _queuedRunJobs);
        Log.JobDropped(_logger, job.QueueId, job.SequenceId, _limits.MaxQueuedJobs);
        return;
      }

      if (!_channel.Writer.TryWrite(NotificationWork.ForJob(job))) {
        Interlocked.Decrement(ref _queuedRunJobs);
      }
    }
    catch (Exception ex) {
      Log.EnqueueFaulted(_logger, ex.GetType().Name);
    }
  }

  public void ResetStreaks(string queueId) {
    try {
      if (string.IsNullOrWhiteSpace(queueId)) return;
      _channel.Writer.TryWrite(NotificationWork.ForReset(queueId));
    }
    catch (Exception ex) {
      Log.EnqueueFaulted(_logger, ex.GetType().Name);
    }
  }

  /// <summary>The worker calls this when it has handled one run job.</summary>
  internal void MarkJobHandled() => Interlocked.Decrement(ref _queuedRunJobs);

  /// <summary>Stops the channel. The worker ends after it reads the last item.</summary>
  internal void Complete() => _channel.Writer.TryComplete();

  private static partial class Log {
    [LoggerMessage(EventId = 12020, Level = LogLevel.Warning, Message = "Notification dropped: {Cap} jobs already wait. Queue {QueueId}, sequence {SequenceId}.")]
    public static partial void JobDropped(ILogger logger, string queueId, string sequenceId, int cap);

    [LoggerMessage(EventId = 12021, Level = LogLevel.Warning, Message = "A notification job could not be queued. Error type: {ErrorType}.")]
    public static partial void EnqueueFaulted(ILogger logger, string errorType);
  }
}
