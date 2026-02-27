using GameBot.Service.Services.ExecutionLog;

namespace GameBot.Service.Hosted;

internal sealed partial class ExecutionLogRetentionCleanupService : BackgroundService
{
  private readonly IServiceProvider _serviceProvider;
  private readonly ILogger<ExecutionLogRetentionCleanupService> _logger;

  public ExecutionLogRetentionCleanupService(IServiceProvider serviceProvider, ILogger<ExecutionLogRetentionCleanupService> logger)
  {
    _serviceProvider = serviceProvider;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        using var scope = _serviceProvider.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<IExecutionLogService>();
        var policy = await svc.GetRetentionAsync(stoppingToken).ConfigureAwait(false);
        var deleted = await svc.CleanupExpiredAsync(stoppingToken).ConfigureAwait(false);
        if (deleted > 0)
        {
          Log.CleanupRemoved(_logger, deleted);
        }

        await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, policy.CleanupIntervalMinutes)), stoppingToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException)
      {
        break;
      }
      catch (Exception ex)
      {
        Log.CleanupFailed(_logger, ex);
        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken).ConfigureAwait(false);
      }
    }
  }

  private static partial class Log
  {
    [LoggerMessage(EventId = 7100, Level = LogLevel.Information, Message = "Execution log cleanup removed {Count} expired entries.")]
    public static partial void CleanupRemoved(ILogger logger, int count);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Warning, Message = "Execution log cleanup cycle failed.")]
    public static partial void CleanupFailed(ILogger logger, Exception ex);
  }
}
