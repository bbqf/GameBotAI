using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using Microsoft.Extensions.Logging;

namespace GameBot.Emulator.Session;

/// <summary>
/// Manages per-session background capture loops that continuously capture ADB screenshots
/// and cache the latest frame in both PNG and Bitmap formats for instant consumer access.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class BackgroundScreenCaptureService : IDisposable
{
    private readonly ConcurrentDictionary<string, SessionCaptureLoop> _loops = new(StringComparer.OrdinalIgnoreCase);
    private readonly Func<string, IAdbScreenCaptureProvider> _captureProviderFactory;
    private volatile int _captureIntervalMs;
    private readonly ILogger<BackgroundScreenCaptureService> _logger;
    private bool _disposed;

    /// <summary>
    /// Creates a new background screen capture service.
    /// </summary>
    /// <param name="captureProviderFactory">Factory that creates an ADB capture provider for a given device serial.</param>
    /// <param name="captureIntervalMs">Target capture interval in milliseconds (minimum 50ms).</param>
    /// <param name="logger">Logger instance.</param>
    public BackgroundScreenCaptureService(
        Func<string, IAdbScreenCaptureProvider> captureProviderFactory,
        int captureIntervalMs,
        ILogger<BackgroundScreenCaptureService> logger)
    {
        _captureProviderFactory = captureProviderFactory ?? throw new ArgumentNullException(nameof(captureProviderFactory));
        _captureIntervalMs = Math.Max(50, captureIntervalMs);
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Starts a background capture loop for the given session and device.</summary>
    public void StartCapture(string sessionId, string deviceSerial)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
        ObjectDisposedException.ThrowIf(_disposed, this);

        // Stop existing loop for this session if any
        if (_loops.TryRemove(sessionId, out var existing))
        {
            existing.Dispose();
            BackgroundCaptureLog.LoopRestarted(_logger, sessionId);
        }

        var provider = _captureProviderFactory(deviceSerial);
        var loop = new SessionCaptureLoop(sessionId, deviceSerial, provider, _captureIntervalMs, _logger);
        _loops[sessionId] = loop;
        loop.Start();
        BackgroundCaptureLog.LoopStarted(_logger, sessionId, deviceSerial, _captureIntervalMs);
    }

    /// <summary>Stops the capture loop for the given session and releases its resources.</summary>
    public void StopCapture(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        if (_loops.TryRemove(sessionId, out var loop))
        {
            loop.Dispose();
            BackgroundCaptureLog.LoopStopped(_logger, sessionId);
        }
    }

    /// <summary>Returns the latest cached frame for a session, or null if unavailable.</summary>
    public CachedFrame? GetCachedFrame(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        return _loops.TryGetValue(sessionId, out var loop) ? loop.CurrentFrame : null;
    }

    /// <summary>Returns capture metrics for a session, or null if no loop exists.</summary>
    public CaptureMetrics? GetCaptureMetrics(string sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return null;
        return _loops.TryGetValue(sessionId, out var loop) ? loop.GetMetrics() : null;
    }

    /// <summary>Updates the capture interval for all active and future capture loops.</summary>
    public void UpdateCaptureInterval(int intervalMs)
    {
        var clamped = Math.Max(50, intervalMs);
        _captureIntervalMs = clamped;
        foreach (var loop in _loops.Values)
        {
            loop.UpdateInterval(clamped);
        }
    }

    /// <summary>Stops all capture loops and releases resources.</summary>
    public void StopAll()
    {
        foreach (var kvp in _loops.ToArray())
        {
            if (_loops.TryRemove(kvp.Key, out var loop))
            {
                loop.Dispose();
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopAll();
    }
}

/// <summary>Abstracts the ADB screenshot capture for testability.</summary>
public interface IAdbScreenCaptureProvider
{
    /// <summary>Captures a screenshot as PNG bytes from the bound device.</summary>
    Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct);
}

/// <summary>Production ADB capture provider using AdbClient.</summary>
[SupportedOSPlatform("windows")]
public sealed class AdbScreenCaptureProvider : IAdbScreenCaptureProvider
{
    private readonly AdbClient _adb;

    public AdbScreenCaptureProvider(string deviceSerial, ILogger<AdbClient> adbLogger)
    {
        _adb = new AdbClient(adbLogger).WithSerial(deviceSerial);
    }

    public async Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct)
    {
        var png = await _adb.GetScreenshotPngAsync(ct).ConfigureAwait(false);
        return png is { Length: > 0 } ? png : null;
    }
}

/// <summary>Manages a single background capture loop for one session.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SessionCaptureLoop : IDisposable
{
    private readonly string _sessionId;
    private readonly string _deviceSerial;
    private readonly IAdbScreenCaptureProvider _provider;
    private volatile int _intervalMs;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();

    // Rolling FPS: circular buffer of last 10 capture durations
    private const int RollingWindowSize = 10;
    private readonly long[] _captureDurationsMs = new long[RollingWindowSize];
    private int _rollingIndex;
    private int _rollingCount;
    private readonly object _metricsLock = new();

    private long _frameCount;
    private Task? _loopTask;

    /// <summary>Latest captured frame; read via Volatile.Read for lock-free access.</summary>
    private CachedFrame? _currentFrame;

    public CachedFrame? CurrentFrame => Volatile.Read(ref _currentFrame);

    public SessionCaptureLoop(
        string sessionId,
        string deviceSerial,
        IAdbScreenCaptureProvider provider,
        int intervalMs,
        ILogger logger)
    {
        _sessionId = sessionId;
        _deviceSerial = deviceSerial;
        _provider = provider;
        _intervalMs = intervalMs;
        _logger = logger;
    }

    public void Start()
    {
        _loopTask = Task.Factory.StartNew(
            () => RunLoopAsync(_cts.Token),
            _cts.Token,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
    }

    public void Stop()
    {
        _cts.Cancel();
        try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* swallow */ }
        // Dispose the last cached bitmap
        var lastFrame = Interlocked.Exchange(ref _currentFrame, null);
        lastFrame?.Bitmap.Dispose();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    /// <summary>Updates the capture interval for this loop. Takes effect on the next iteration.</summary>
    public void UpdateInterval(int intervalMs) => _intervalMs = Math.Max(50, intervalMs);

    public CaptureMetrics GetMetrics()
    {
        double? fps;
        lock (_metricsLock)
        {
            fps = ComputeFps();
        }
        var frame = CurrentFrame;
        return new CaptureMetrics(fps, Interlocked.Read(ref _frameCount), frame?.Timestamp);
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                var png = await _provider.CaptureScreenshotPngAsync(ct).ConfigureAwait(false);
                if (png is not null && !ct.IsCancellationRequested)
                {
                    Bitmap? bitmap = null;
                    try
                    {
                        using var ms = new MemoryStream(png, writable: false);
                        using var tmp = new Bitmap(ms);
#pragma warning disable CA2000 // Bitmap ownership transfers to CachedFrame; disposed when frame is swapped out
                        bitmap = new Bitmap(tmp); // detach from stream
#pragma warning restore CA2000
                    }
                    catch
                    {
                        bitmap?.Dispose();
                        throw;
                    }

                    var frame = new CachedFrame(png, bitmap, DateTimeOffset.UtcNow, bitmap.Width, bitmap.Height);
                    var oldFrame = Interlocked.Exchange(ref _currentFrame, frame);
                    oldFrame?.Bitmap.Dispose();
                    Interlocked.Increment(ref _frameCount);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                BackgroundCaptureLog.CaptureError(_logger, _sessionId, ex);
            }

            sw.Stop();
            RecordCaptureDuration(sw.ElapsedMilliseconds);

            // Delay remainder of interval (no overlapping captures)
            var elapsed = (int)sw.ElapsedMilliseconds;
            var delay = Math.Max(0, _intervalMs - elapsed);
            if (delay > 0 && !ct.IsCancellationRequested)
            {
                try { await Task.Delay(delay, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void RecordCaptureDuration(long durationMs)
    {
        lock (_metricsLock)
        {
            _captureDurationsMs[_rollingIndex] = durationMs;
            _rollingIndex = (_rollingIndex + 1) % RollingWindowSize;
            if (_rollingCount < RollingWindowSize) _rollingCount++;
        }
    }

    private double? ComputeFps()
    {
        if (_rollingCount == 0) return null;
        long total = 0;
        for (var i = 0; i < _rollingCount; i++) total += _captureDurationsMs[i];
        // When capture is near-instant, avgDuration can be 0; use configured interval as floor
        var avgDuration = _rollingCount > 0 ? (double)total / _rollingCount : 0;
        var effectiveInterval = Math.Max(avgDuration, _intervalMs);
        return 1000.0 / effectiveInterval;
    }
}

internal static partial class BackgroundCaptureLog
{
    [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Background capture loop started for session {SessionId} device {DeviceSerial} interval {IntervalMs}ms")]
    public static partial void LoopStarted(ILogger logger, string sessionId, string deviceSerial, int intervalMs);

    [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Background capture loop stopped for session {SessionId}")]
    public static partial void LoopStopped(ILogger logger, string sessionId);

    [LoggerMessage(EventId = 5003, Level = LogLevel.Debug, Message = "Background capture loop restarted for session {SessionId}")]
    public static partial void LoopRestarted(ILogger logger, string sessionId);

    [LoggerMessage(EventId = 5004, Level = LogLevel.Debug, Message = "Background capture failed for session {SessionId}")]
    public static partial void CaptureError(ILogger logger, string sessionId, Exception ex);
}
