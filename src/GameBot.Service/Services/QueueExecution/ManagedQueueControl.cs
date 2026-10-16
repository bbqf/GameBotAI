using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Queues;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// The port of the managed gamepack update over the queue engine (feature 148). It lists the queues with their state,
/// stops a queue with a time limit, and starts a queue.
/// </summary>
internal sealed class ManagedQueueControl : IManagedQueueControl {
  private readonly IQueueRepository _queues;
  private readonly IQueueExecutionService _execution;

  public ManagedQueueControl(IQueueRepository queues, IQueueExecutionService execution) {
    _queues = queues ?? throw new ArgumentNullException(nameof(queues));
    _execution = execution ?? throw new ArgumentNullException(nameof(execution));
  }

  public async Task<IReadOnlyList<ManagedQueueInfo>> ListAsync(CancellationToken ct = default) {
    var all = await _queues.ListAsync().ConfigureAwait(false);
    return all.Select(q => new ManagedQueueInfo(q.Id, q.Name, q.LinkedTemplateId, _execution.IsRunning(q.Id))).ToList();
  }

  public async Task<bool> StopAsync(string queueId, TimeSpan limit, CancellationToken ct = default) {
    var stopping = _execution.StopAsync(queueId, ct);
    try {
      await stopping.WaitAsync(limit, ct).ConfigureAwait(false);
      return true;
    }
    catch (TimeoutException) {
      // The stop goes on in the background. The task is observed, so a late failure is not an unobserved exception.
      _ = stopping.ContinueWith(
        static t => _ = t.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);
      return false;
    }
  }

  public async Task<string?> StartAsync(string queueId, CancellationToken ct = default) {
    var outcome = await _execution.StartAsync(queueId, ct).ConfigureAwait(false);
    return outcome switch {
      QueueStartOutcome.Started => null,
      QueueStartOutcome.AlreadyRunning => null,
      QueueStartOutcome.NotFound => "The queue does not exist.",
      QueueStartOutcome.DeviceInUse => "The device of the queue is in use by another queue.",
      _ => "The queue did not start."
    };
  }
}
