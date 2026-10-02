using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services.StepThrough;

/// <summary>
/// Ends the step-throughs whose lease ended (feature 127, R7). Every 10 seconds it asks the service to
/// cancel a running step, resume a queue that the step-through paused, and remove the step-through. A
/// view that sends no read for 90 seconds loses its step-through at most 100 seconds after its last read.
/// </summary>
internal sealed class StepThroughLeaseSweeper : BackgroundService {
  /// <summary>How often the sweeper checks the leases.</summary>
  public static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(10);

  private readonly IStepThroughService _service;
  private readonly TimeProvider _time;
  private readonly ILogger<StepThroughLeaseSweeper>? _logger;

  public StepThroughLeaseSweeper(IStepThroughService service, TimeProvider time, ILogger<StepThroughLeaseSweeper>? logger = null) {
    _service = service;
    _time = time;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    using var timer = new PeriodicTimer(SweepInterval, _time);
    try {
      while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false)) {
        await SweepOnceAsync().ConfigureAwait(false);
      }
    }
    catch (OperationCanceledException) {
      // The service stops. A step-through that remains ends with the process.
    }
  }

  /// <summary>One sweep. A failure of one sweep does not stop the next one.</summary>
  internal async Task SweepOnceAsync() {
    try {
      await _service.SweepExpiredAsync().ConfigureAwait(false);
    }
    catch (Exception ex) {
      if (_logger is not null) StepThroughSweepLog.SweepFailed(_logger, ex);
    }
  }
}

internal static partial class StepThroughSweepLog {
  [LoggerMessage(EventId = 1173, Level = LogLevel.Warning, Message = "The step-through lease sweep failed")]
  public static partial void SweepFailed(ILogger logger, Exception ex);
}
