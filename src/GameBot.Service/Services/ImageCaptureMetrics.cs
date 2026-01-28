using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace GameBot.Service.Services;

internal interface IImageCaptureMetrics
{
    long SuccessCount { get; }
    long FailureCount { get; }
    long AccuracyFailures { get; }
    long LastDurationMs { get; }
    long P95DurationMs { get; }
    void RecordCaptureResult(long durationMs, bool success, bool withinOnePixel);
}

internal sealed class ImageCaptureMetrics : IImageCaptureMetrics
{
    private const int WindowSize = 20;
    private static readonly Action<ILogger, long, bool, bool, Exception?> _captureResult =
        LoggerMessage.Define<long, bool, bool>(LogLevel.Trace, new EventId(4001, "CaptureResult"), "Capture->crop->save completed in {DurationMs}ms (success: {Success}, within-1px: {WithinOnePixel})");

    private readonly ILogger<ImageCaptureMetrics> _logger;
    private readonly ConcurrentQueue<long> _durations = new();
    private long _success;
    private long _failure;
    private long _accuracyFailures;
    private long _lastDuration;

    public ImageCaptureMetrics(ILogger<ImageCaptureMetrics> logger)
    {
        _logger = logger;
    }

    public long SuccessCount => Interlocked.Read(ref _success);
    public long FailureCount => Interlocked.Read(ref _failure);
    public long AccuracyFailures => Interlocked.Read(ref _accuracyFailures);
    public long LastDurationMs => Interlocked.Read(ref _lastDuration);
    public long P95DurationMs => CalculateP95();

    public void RecordCaptureResult(long durationMs, bool success, bool withinOnePixel)
    {
        Interlocked.Exchange(ref _lastDuration, durationMs);
        if (success)
        {
            Interlocked.Increment(ref _success);
        }
        else
        {
            Interlocked.Increment(ref _failure);
        }

        if (!withinOnePixel)
        {
            Interlocked.Increment(ref _accuracyFailures);
        }

        EnqueueDuration(durationMs);
        _captureResult(_logger, durationMs, success, withinOnePixel, null);
    }

    private void EnqueueDuration(long durationMs)
    {
        _durations.Enqueue(durationMs);
        while (_durations.Count > WindowSize && _durations.TryDequeue(out _))
        {
        }
    }

    private long CalculateP95()
    {
        var snapshot = _durations.ToArray();
        if (snapshot.Length == 0) return 0;
        var ordered = snapshot.OrderBy(x => x).ToArray();
        var index = (int)System.Math.Ceiling(0.95 * ordered.Length) - 1;
        return ordered[System.Math.Max(index, 0)];
    }
}
