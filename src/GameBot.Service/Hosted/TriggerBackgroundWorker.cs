using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Hosted;

internal sealed class TriggerBackgroundWorker : BackgroundService
{
    private readonly ILogger<TriggerBackgroundWorker> _logger;
    private readonly TriggerEvaluationService _service;
    private readonly TimeSpan _interval;

    public TriggerBackgroundWorker(
        ILogger<TriggerBackgroundWorker> logger,
        TriggerEvaluationService service)
    {
        _logger = logger;
        _service = service;
        _interval = TimeSpan.FromSeconds(2);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
    Log.WorkerStarted(_logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            // TODO: iterate active profiles/triggers and evaluate (stub)

            try
            {
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

    public static void WorkerStarted(ILogger l) => _workerStarted(l, null);
    public static void WorkerStopped(ILogger l) => _workerStopped(l, null);
    // Intentionally no catch-all log to satisfy CA1031; errors propagate to host if any occur
}
