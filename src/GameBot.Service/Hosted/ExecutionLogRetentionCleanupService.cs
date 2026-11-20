using System.Diagnostics;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;

namespace GameBot.Service.Hosted;

internal sealed partial class ExecutionLogRetentionCleanupService : BackgroundService {
  private readonly IServiceProvider _serviceProvider;
  private readonly ILogger<ExecutionLogRetentionCleanupService> _logger;
  private readonly DateTimeOffset _processStartUtc;

  public ExecutionLogRetentionCleanupService(IServiceProvider serviceProvider, ILogger<ExecutionLogRetentionCleanupService> logger)
    : this(serviceProvider, logger, GetProcessStartUtc()) {
  }

  /// <summary>Test constructor. Sets the process start time that the start sweep uses.</summary>
  internal ExecutionLogRetentionCleanupService(IServiceProvider serviceProvider, ILogger<ExecutionLogRetentionCleanupService> logger, DateTimeOffset processStartUtc) {
    _serviceProvider = serviceProvider;
    _logger = logger;
    _processStartUtc = processStartUtc;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    // Feature 198 (FR-006): one time at start, close the "running" entries of an earlier service process.
    // The sweep runs also when the retention policy is off.
    try {
      using var scope = _serviceProvider.CreateScope();
      var repository = scope.ServiceProvider.GetRequiredService<IExecutionLogRepository>();
      var closed = await repository.CloseAbandonedRunningAsync(_processStartUtc, stoppingToken).ConfigureAwait(false);
      if (closed > 0) {
        Log.StartSweepClosed(_logger, closed);
      }
    }
    catch (OperationCanceledException) {
      return;
    }
    catch (Exception ex) {
      Log.StartSweepFailed(_logger, ex);
    }

    while (!stoppingToken.IsCancellationRequested) {
      try {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExecutionLogService>();
        var policy = await svc.GetRetentionAsync(stoppingToken).ConfigureAwait(false);
        var deleted = await svc.CleanupExpiredAsync(stoppingToken).ConfigureAwait(false);
        if (deleted > 0) {
          Log.CleanupRemoved(_logger, deleted);
        }

        await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, policy.CleanupIntervalMinutes)), stoppingToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        break;
      }
      catch (Exception ex) {
        Log.CleanupFailed(_logger, ex);
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
      }
    }
  }

  private static DateTimeOffset GetProcessStartUtc() {
    using var process = Process.GetCurrentProcess();
    return new DateTimeOffset(process.StartTime.ToUniversalTime(), TimeSpan.Zero);
  }

  private static partial class Log {
    [LoggerMessage(EventId = 7100, Level = LogLevel.Information, Message = "Execution log cleanup removed {Count} expired entries.")]
    public static partial void CleanupRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Execution log cleanup cycle failed.")]
    public static partial void CleanupFailed(ILogger logger, Exception ex);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Information, Message = "Execution log start sweep closed {Count} running entries of an earlier service process.")]
    public static partial void StartSweepClosed(ILogger logger, int count);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Warning, Message = "Execution log start sweep failed.")]
    public static partial void StartSweepFailed(ILogger logger, Exception ex);
  }
}
