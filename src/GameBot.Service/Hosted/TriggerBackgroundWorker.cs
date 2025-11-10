using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Hosted;

internal sealed class TriggerBackgroundWorker : BackgroundService
{
    private readonly ILogger<TriggerBackgroundWorker> _logger;
    private readonly TriggerEvaluationCoordinator _coordinator;
    private readonly TimeSpan _interval;
    private readonly string? _gameFilter;

    public TriggerBackgroundWorker(
        ILogger<TriggerBackgroundWorker> logger,
        TriggerEvaluationCoordinator coordinator)
    {
        _logger = logger;
        _coordinator = coordinator;
        _interval = TimeSpan.FromSeconds(2);
        _gameFilter = Environment.GetEnvironmentVariable("GAMEBOT_EVAL_GAME_ID");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.WorkerStarted(_logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var count = await _coordinator.EvaluateAllAsync(_gameFilter, stoppingToken).ConfigureAwait(false);
                Log.WorkerCycle(_logger, count);
                await Task.Delay(_interval, stoppingToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException) { break; }
        }
        Log.WorkerStopped(_logger);
    }
}

internal static class Log
{
    private static readonly Action<ILogger, Exception?> _workerStarted =
        LoggerMessage.Define(LogLevel.Information, new EventId(3001, nameof(WorkerStarted)), "Trigger background worker started");
    private static readonly Action<ILogger, Exception?> _workerStopped =
        LoggerMessage.Define(LogLevel.Information, new EventId(3002, nameof(WorkerStopped)), "Trigger background worker stopped");
    private static readonly Action<ILogger, int, Exception?> _workerCycle =
        LoggerMessage.Define<int>(LogLevel.Debug, new EventId(3003, nameof(WorkerCycle)), "Evaluated {TriggerCount} trigger(s) this cycle");

    public static void WorkerStarted(ILogger l) => _workerStarted(l, null);
    public static void WorkerStopped(ILogger l) => _workerStopped(l, null);
    public static void WorkerCycle(ILogger l, int count) => _workerCycle(l, count, null);
    // Intentionally no catch-all log to satisfy CA1031; errors propagate to host if any occur
}
