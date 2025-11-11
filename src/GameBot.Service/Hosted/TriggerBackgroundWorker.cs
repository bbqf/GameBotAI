using GameBot.Domain.Profiles;
using GameBot.Domain.Services;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace GameBot.Service.Hosted;

internal sealed class TriggerBackgroundWorker : BackgroundService
{
    private readonly ILogger<TriggerBackgroundWorker> _logger;
    private readonly ITriggerEvaluationCoordinator _coordinator;
    private readonly ISessionManager _sessions;
    private readonly IOptionsMonitor<TriggerWorkerOptions> _options;
    private readonly ITriggerEvaluationMetrics _metrics;
    private int _running;

    public TriggerBackgroundWorker(
        ILogger<TriggerBackgroundWorker> logger,
    ITriggerEvaluationCoordinator coordinator,
        ISessionManager sessions,
        IOptionsMonitor<TriggerWorkerOptions> options,
        ITriggerEvaluationMetrics metrics)
    {
        _logger = logger;
        _coordinator = coordinator;
        _sessions = sessions;
        _options = options;
        _metrics = metrics;
        _running = 0;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.WorkerStarted(_logger);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var opts = _options.CurrentValue;
                var interval = TimeSpan.FromSeconds(Math.Max(1, opts.IntervalSeconds));
                var backoff = TimeSpan.FromSeconds(Math.Max(1, opts.IdleBackoffSeconds));

                if (opts.SkipWhenNoSessions && _sessions.ActiveCount <= 0)
                {
                    Log.CycleSkippedNoSessions(_logger);
                    _metrics.IncrementSkippedNoSessions();
                    await Task.Delay(backoff, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                if (Interlocked.Exchange(ref _running, 1) == 1)
                {
                    Log.CycleOverlapSkipped(_logger);
                    _metrics.IncrementOverlapSkipped();
                    await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
                    continue;
                }

                var sw = Stopwatch.StartNew();
                try
                {
                    var count = await _coordinator.EvaluateAllAsync(opts.GameFilter, stoppingToken).ConfigureAwait(false);
                    if (count > 0)
                    {
                        _metrics.IncrementEvaluations(count);
                    }
                    Log.WorkerCycle(_logger, count);
                }
                finally
                {
                    sw.Stop();
                    Log.CycleDuration(_logger, sw.ElapsedMilliseconds);
                    _metrics.RecordCycleDuration(sw.ElapsedMilliseconds);
                    Interlocked.Exchange(ref _running, 0);
                }

                await Task.Delay(interval, stoppingToken).ConfigureAwait(false);
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
    private static readonly Action<ILogger, Exception?> _cycleSkippedNoSessions =
        LoggerMessage.Define(LogLevel.Debug, new EventId(3004, nameof(CycleSkippedNoSessions)), "Skipped evaluation cycle: no active sessions");
    private static readonly Action<ILogger, Exception?> _cycleOverlapSkipped =
        LoggerMessage.Define(LogLevel.Debug, new EventId(3005, nameof(CycleOverlapSkipped)), "Skipped evaluation cycle: previous cycle still running");
    private static readonly Action<ILogger, long, Exception?> _cycleDuration =
        LoggerMessage.Define<long>(LogLevel.Trace, new EventId(3006, nameof(CycleDuration)), "Evaluation cycle duration {DurationMs}ms");

    public static void WorkerStarted(ILogger l) => _workerStarted(l, null);
    public static void WorkerStopped(ILogger l) => _workerStopped(l, null);
    public static void WorkerCycle(ILogger l, int count) => _workerCycle(l, count, null);
    public static void CycleSkippedNoSessions(ILogger l) => _cycleSkippedNoSessions(l, null);
    public static void CycleOverlapSkipped(ILogger l) => _cycleOverlapSkipped(l, null);
    public static void CycleDuration(ILogger l, long ms) => _cycleDuration(l, ms, null);
    // Intentionally no catch-all log to satisfy CA1031; errors propagate to host if any occur
}
