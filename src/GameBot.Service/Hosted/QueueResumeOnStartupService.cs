using GameBot.Domain.Queues;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.Service.Hosted;

/// <summary>
/// Starts again, once the service is up, every queue that was Running when the service stopped and
/// opts in with <see cref="ExecutionQueue.ResumeOnServiceStart"/> (feature 098, #203).
/// <para>
/// Production queues are built to never end — every task reschedules itself and the queue idles
/// between firings — so the only thing that ends them is the service itself. Without this, a restart,
/// an upgrade or a host reboot leaves them Stopped until someone notices; that has already cost 44
/// hours of a bot reporting nothing while doing nothing. An external watchdog that calls start is ruled
/// out, so the service brings its own queues back.
/// </para>
/// <para>
/// One pass per service start, after <see cref="IHostApplicationLifetime.ApplicationStarted"/>, so each
/// run starts in the same fully started host a manual start would. Each recorded queue gets one
/// attempt, and its outcome is logged. A queue that is gone, does not opt in, or cannot be started loses
/// its record, so it is not attempted again on a later start either.
/// </para>
/// </summary>
internal sealed partial class QueueResumeOnStartupService : BackgroundService {
  // Resolved at pass time rather than injected, for the same reason as QueueDeviceWatchdogService: a
  // hosted service is constructed while the host starts, and depending on the queue-execution graph
  // would build all of it ahead of everything that expects to build lazily.
  private readonly IServiceProvider _serviceProvider;
  private readonly IHostApplicationLifetime _lifetime;
  private readonly ILogger<QueueResumeOnStartupService> _logger;

  public QueueResumeOnStartupService(
      IServiceProvider serviceProvider,
      IHostApplicationLifetime lifetime,
      ILogger<QueueResumeOnStartupService> logger) {
    _serviceProvider = serviceProvider;
    _lifetime = lifetime;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    try {
      await WaitForApplicationStartedAsync(stoppingToken).ConfigureAwait(false);
      using var scope = _serviceProvider.CreateScope();
      await ResumeAsync(
        scope.ServiceProvider.GetRequiredService<IQueueRunStateStore>(),
        scope.ServiceProvider.GetRequiredService<IQueueRepository>(),
        () => scope.ServiceProvider.GetRequiredService<IQueueExecutionService>(),
        stoppingToken).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) {
      // The service is stopping before or during the pass; unprocessed records stay for the next start.
    }
  }

  private async Task WaitForApplicationStartedAsync(CancellationToken stoppingToken) {
    if (_lifetime.ApplicationStarted.IsCancellationRequested) return;
    var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    using var onStarted = _lifetime.ApplicationStarted.Register(() => started.TrySetResult());
    using var onStopping = stoppingToken.Register(() => started.TrySetCanceled(stoppingToken));
    await started.Task.ConfigureAwait(false);
  }

  /// <summary>
  /// The resume pass: starts each recorded queue that still exists and opts in, and drops the record of
  /// every other one. One queue's failure never stops the pass; cancellation stops it between queues and
  /// leaves the remaining records for the next start.
  /// <para>
  /// The execution service is resolved only when a queue is actually about to be started. Building it
  /// pulls in the device and screen-capture graph, and a service start with nothing to resume — the
  /// common case — must leave that graph to build lazily on first use, exactly as before this feature.
  /// </para>
  /// </summary>
  internal async Task ResumeAsync(
      IQueueRunStateStore runState,
      IQueueRepository queues,
      Func<IQueueExecutionService> executionFactory,
      CancellationToken ct) {
    IQueueExecutionService? execution = null;
    IReadOnlyList<string> recorded;
    try {
      recorded = await runState.ListRunningAsync().ConfigureAwait(false);
    }
    catch (Exception ex) {
      Log.RecordUnreadable(_logger, ex);
      return;
    }

    foreach (var queueId in recorded) {
      ct.ThrowIfCancellationRequested();
      try {
        var queue = await queues.GetAsync(queueId).ConfigureAwait(false);
        if (queue is null) {
          Log.QueueGone(_logger, queueId);
          await ForgetAsync(runState, queueId).ConfigureAwait(false);
          continue;
        }
        if (!queue.ResumeOnServiceStart) {
          Log.NotOptedIn(_logger, queueId);
          await ForgetAsync(runState, queueId).ConfigureAwait(false);
          continue;
        }

        execution ??= executionFactory();
        var outcome = await execution.StartAsync(queueId, ct).ConfigureAwait(false);
        Log.Resumed(_logger, queueId, outcome);
        // Started keeps the record (the new run owns it); AlreadyRunning means a run already owns it.
        if (outcome is QueueStartOutcome.NotFound or QueueStartOutcome.DeviceInUse) {
          await ForgetAsync(runState, queueId).ConfigureAwait(false);
        }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested) {
        throw;
      }
      catch (Exception ex) {
        Log.ResumeFailed(_logger, queueId, ex);
        await ForgetAsync(runState, queueId).ConfigureAwait(false);
      }
    }
  }

  // Best effort: a record that cannot be dropped is simply looked at again on the next start.
  private static async Task ForgetAsync(IQueueRunStateStore runState, string queueId) {
    try {
      await runState.ClearAsync(queueId).ConfigureAwait(false);
    }
    catch (Exception) {
      // Nothing more to do; the pass has already logged what happened to this queue.
    }
  }

  private static partial class Log {
    [LoggerMessage(EventId = 7300, Level = LogLevel.Information, Message = "Queue {QueueId} was running when the service stopped; resume outcome: {Outcome}.")]
    public static partial void Resumed(ILogger logger, string queueId, QueueStartOutcome outcome);

    [LoggerMessage(EventId = 7301, Level = LogLevel.Information, Message = "Queue {QueueId} was running when the service stopped but does not opt in to resume; leaving it stopped.")]
    public static partial void NotOptedIn(ILogger logger, string queueId);

    [LoggerMessage(EventId = 7302, Level = LogLevel.Information, Message = "Queue {QueueId} was recorded as running but no longer exists; discarding the record.")]
    public static partial void QueueGone(ILogger logger, string queueId);

    [LoggerMessage(EventId = 7303, Level = LogLevel.Error, Message = "Queue {QueueId} could not be resumed after the service restart.")]
    public static partial void ResumeFailed(ILogger logger, string queueId, Exception ex);

    [LoggerMessage(EventId = 7304, Level = LogLevel.Warning, Message = "Resume pass could not read the running-queue record; no queues resumed.")]
    public static partial void RecordUnreadable(ILogger logger, Exception ex);
  }
}
