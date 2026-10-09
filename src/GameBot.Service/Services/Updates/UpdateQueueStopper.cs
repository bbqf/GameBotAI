using GameBot.Domain.Queues;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Stops all running queues at once, without a wait for idle (clarification Q2, FR-013).
/// The services are resolved at call time, because the queue-execution graph is heavy and
/// the update feature must not build it at start.
/// <para>
/// The coordinator calls this after <c>StopApplication()</c>. The host stopping token is then already
/// cancelled, so the queue engine keeps the "running" record of each queue. The queues with
/// <c>resumeOnServiceStart</c> start again after the restart (feature 098).
/// </para>
/// </summary>
internal sealed class UpdateQueueStopper : IUpdateQueueStopper {
  private readonly IServiceProvider _services;
  private readonly ILogger<UpdateQueueStopper> _logger;

  public UpdateQueueStopper(IServiceProvider services, ILogger<UpdateQueueStopper> logger) {
    _services = services;
    _logger = logger;
  }

  public async Task StopAllAsync(CancellationToken ct) {
    using var scope = _services.CreateScope();
    var queues = scope.ServiceProvider.GetRequiredService<IQueueRepository>();
    var execution = scope.ServiceProvider.GetRequiredService<IQueueExecutionService>();

    var all = await queues.ListAsync().ConfigureAwait(false);
    foreach (var queue in all) {
      ct.ThrowIfCancellationRequested();
      if (!execution.IsRunning(queue.Id)) {
        continue;
      }

      try {
        await execution.StopAsync(queue.Id, ct).ConfigureAwait(false);
        UpdateLog.QueueStopped(_logger, queue.Id);
      }
      catch (Exception ex) when (ex is not OperationCanceledException) {
        UpdateLog.QueueStopFailed(_logger, ex.Message);
      }
    }
  }
}
