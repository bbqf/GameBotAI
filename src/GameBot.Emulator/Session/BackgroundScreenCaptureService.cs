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
public sealed class BackgroundScreenCaptureService : IDisposable {
  private readonly ConcurrentDictionary<string, SessionCaptureLoop> _loops = new(StringComparer.OrdinalIgnoreCase);
  private readonly Func<string, IAdbScreenCaptureProvider> _captureProviderFactory;
  private volatile int _captureIntervalMs;
  private readonly ILogger<BackgroundScreenCaptureService> _logger;
  // Feature 106: the liveness data of each session, and the time limit of one capture.
  private readonly IDeviceLivenessTracker? _tracker;
  private readonly int _captureTimeoutMs;
  // Feature 121: at most one unfinished capture for each device, and the pace of the device check.
  private readonly DeviceCaptureGate _gate;
  private readonly int _suspectCheckIntervalMs;
  private readonly int _transportCheckTimeoutMs;
  private bool _disposed;

  /// <summary>
  /// Creates a new background screen capture service.
  /// </summary>
  /// <param name="captureProviderFactory">Factory that creates an ADB capture provider for a given device serial.</param>
  /// <param name="captureIntervalMs">Target capture interval in milliseconds (minimum 50ms).</param>
  /// <param name="logger">Logger instance.</param>
  /// <param name="tracker">Optional. Receives the capture data of each loop (feature 106).</param>
  /// <param name="livenessOptions">Optional. Gives the time limit of one capture (feature 106).</param>
  /// <param name="gate">Optional. The capture gate of the service (feature 121). A private gate is used when null.</param>
  public BackgroundScreenCaptureService(
      Func<string, IAdbScreenCaptureProvider> captureProviderFactory,
      int captureIntervalMs,
      ILogger<BackgroundScreenCaptureService> logger,
      IDeviceLivenessTracker? tracker = null,
      DeviceLivenessOptions? livenessOptions = null,
      DeviceCaptureGate? gate = null) {
    _captureProviderFactory = captureProviderFactory ?? throw new ArgumentNullException(nameof(captureProviderFactory));
    _captureIntervalMs = Math.Max(50, captureIntervalMs);
    _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    _tracker = tracker;
    var normalized = (livenessOptions ?? new DeviceLivenessOptions()).Normalized();
    _captureTimeoutMs = normalized.CaptureTimeoutMs;
    _suspectCheckIntervalMs = normalized.CaptureStallLimitMs;
    _transportCheckTimeoutMs = normalized.TransportCheckTimeoutMs;
    _gate = gate ?? new DeviceCaptureGate();
  }

  /// <summary>Starts a background capture loop for the given session and device.</summary>
  public void StartCapture(string sessionId, string deviceSerial) {
    ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
    ArgumentException.ThrowIfNullOrWhiteSpace(deviceSerial);
    ObjectDisposedException.ThrowIf(_disposed, this);

    // Stop existing loop for this session if any
    if (_loops.TryRemove(sessionId, out var existing)) {
      existing.Dispose();
      BackgroundCaptureLog.LoopRestarted(_logger, sessionId);
    }

    // Feature 121 (R-013): a new loop for the serial is a repair of the device, so it clears Suspect.
    _gate.Clear(deviceSerial);
    var provider = _captureProviderFactory(deviceSerial);
    var loop = new SessionCaptureLoop(sessionId, deviceSerial, provider, _captureIntervalMs, _logger, _tracker, _captureTimeoutMs,
      _gate, _suspectCheckIntervalMs, _transportCheckTimeoutMs);
    _loops[sessionId] = loop;
    // Before the loop starts, so that the first capture finds a running loop in the tracker.
    _tracker?.LoopStarted(sessionId);
    loop.Start();
    BackgroundCaptureLog.LoopStarted(_logger, sessionId, deviceSerial, _captureIntervalMs);
  }

  /// <summary>Stops the capture loop for the given session and releases its resources.</summary>
  public void StopCapture(string sessionId) {
    if (string.IsNullOrWhiteSpace(sessionId)) return;
    if (_loops.TryRemove(sessionId, out var loop)) {
      _tracker?.LoopStopped(sessionId);
      loop.Dispose();
      BackgroundCaptureLog.LoopStopped(_logger, sessionId);
    }
  }

  /// <summary>Returns the latest cached frame for a session, or null if unavailable.</summary>
  public CachedFrame? GetCachedFrame(string sessionId) {
    if (string.IsNullOrWhiteSpace(sessionId)) return null;
    return _loops.TryGetValue(sessionId, out var loop) ? loop.CurrentFrame : null;
  }

  /// <summary>Returns capture metrics for a session, or null if no loop exists.</summary>
  public CaptureMetrics? GetCaptureMetrics(string sessionId) {
    if (string.IsNullOrWhiteSpace(sessionId)) return null;
    return _loops.TryGetValue(sessionId, out var loop) ? loop.GetMetrics() : null;
  }

  /// <summary>Updates the capture interval for all active and future capture loops.</summary>
  public void UpdateCaptureInterval(int intervalMs) {
    var clamped = Math.Max(50, intervalMs);
    _captureIntervalMs = clamped;
    foreach (var loop in _loops.Values) {
      loop.UpdateInterval(clamped);
    }
  }

  /// <summary>Stops all capture loops and releases resources.</summary>
  public void StopAll() {
    foreach (var kvp in _loops.ToArray()) {
      if (_loops.TryRemove(kvp.Key, out var loop)) {
        _tracker?.LoopStopped(kvp.Key);
        loop.Dispose();
      }
    }
  }

  public void Dispose() {
    if (_disposed) return;
    _disposed = true;
    StopAll();
  }
}

/// <summary>Abstracts the ADB screenshot capture for testability.</summary>
public interface IAdbScreenCaptureProvider {
  /// <summary>Captures a screenshot as PNG bytes from the bound device.</summary>
  Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct);

  /// <summary>
  /// Asks the device if a <c>screencap</c> process still runs (feature 121). Returns true when it runs,
  /// false when it does not, and null when the provider cannot tell. The default answer is null.
  /// </summary>
  Task<bool?> HasRunningScreencapAsync(int timeoutMs, CancellationToken ct) => Task.FromResult<bool?>(null);
}

/// <summary>Production ADB capture provider using AdbClient.</summary>
[SupportedOSPlatform("windows")]
public sealed class AdbScreenCaptureProvider : IAdbScreenCaptureProvider {
  private readonly AdbClient _adb;

  public AdbScreenCaptureProvider(string deviceSerial, ILogger<AdbClient> adbLogger) {
    _adb = new AdbClient(adbLogger).WithSerial(deviceSerial);
  }

  public async Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
    var png = await _adb.GetScreenshotPngAsync(ct).ConfigureAwait(false);
    return png is { Length: > 0 } ? png : null;
  }

  public Task<bool?> HasRunningScreencapAsync(int timeoutMs, CancellationToken ct) =>
    _adb.HasRunningScreencapAsync(timeoutMs, ct);
}

/// <summary>Manages a single background capture loop for one session.</summary>
[SupportedOSPlatform("windows")]
internal sealed class SessionCaptureLoop : IDisposable {
  private readonly string _sessionId;
  private readonly string _deviceSerial;
  private readonly IAdbScreenCaptureProvider _provider;
  private volatile int _intervalMs;
  private readonly ILogger _logger;
  private readonly CancellationTokenSource _cts = new();
  private readonly IDeviceLivenessTracker? _tracker;
  private readonly int _captureTimeoutMs;
  private readonly DeviceCaptureGate _gate;
  private readonly int _suspectCheckIntervalMs;
  private readonly int _transportCheckTimeoutMs;

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
      ILogger logger,
      IDeviceLivenessTracker? tracker = null,
      int captureTimeoutMs = 10000,
      DeviceCaptureGate? gate = null,
      int suspectCheckIntervalMs = 60000,
      int transportCheckTimeoutMs = 5000) {
    _sessionId = sessionId;
    _deviceSerial = deviceSerial;
    _provider = provider;
    _intervalMs = intervalMs;
    _logger = logger;
    _tracker = tracker;
    _captureTimeoutMs = Math.Max(1, captureTimeoutMs);
    _gate = gate ?? new DeviceCaptureGate();
    _suspectCheckIntervalMs = Math.Max(1, suspectCheckIntervalMs);
    _transportCheckTimeoutMs = Math.Max(1, transportCheckTimeoutMs);
  }

  public void Start() {
    _loopTask = Task.Factory.StartNew(
        () => RunLoopAsync(_cts.Token),
        _cts.Token,
        TaskCreationOptions.LongRunning,
        TaskScheduler.Default).Unwrap();
  }

  public void Stop() {
    _cts.Cancel();
    try { _loopTask?.Wait(TimeSpan.FromSeconds(2)); } catch { /* swallow */ }
    // Dispose the last cached bitmap
    var lastFrame = Interlocked.Exchange(ref _currentFrame, null);
    lastFrame?.Bitmap.Dispose();
  }

  public void Dispose() {
    Stop();
    _cts.Dispose();
  }

  /// <summary>Updates the capture interval for this loop. Takes effect on the next iteration.</summary>
  public void UpdateInterval(int intervalMs) => _intervalMs = Math.Max(50, intervalMs);

  public CaptureMetrics GetMetrics() {
    double? fps;
    lock (_metricsLock) {
      fps = ComputeFps();
    }
    var frame = CurrentFrame;
    return new CaptureMetrics(fps, Interlocked.Read(ref _frameCount), frame?.Timestamp);
  }

  private async Task RunLoopAsync(CancellationToken ct) {
    while (!ct.IsCancellationRequested) {
      // Feature 121 (FR-013): at most one unfinished capture for each device. While another capture
      // is unfinished, or the device is Suspect, this loop starts no screencap process.
      if (!_gate.TryBegin(_deviceSerial)) {
        await WaitWhileBlockedAsync(ct).ConfigureAwait(false);
        continue;
      }
      var sw = Stopwatch.StartNew();
      // Feature 106 (research R-009): each capture gets its own time limit. One hung screencap then
      // is one failed capture, and the loop continues. Before, it stopped the loop for all time.
      using var captureLimit = CancellationTokenSource.CreateLinkedTokenSource(ct);
      captureLimit.CancelAfter(_captureTimeoutMs);
      try {
        var png = await _provider.CaptureScreenshotPngAsync(captureLimit.Token).ConfigureAwait(false);
        if (png is null) _gate.Failed(_deviceSerial);
        else _gate.Completed(_deviceSerial);
        if (png is not null && !ct.IsCancellationRequested) {
          Bitmap? bitmap = null;
          try {
            using var ms = new MemoryStream(png, writable: false);
            using var tmp = new Bitmap(ms);
#pragma warning disable CA2000 // Bitmap ownership transfers to CachedFrame; disposed when frame is swapped out
            bitmap = new Bitmap(tmp); // detach from stream
#pragma warning restore CA2000
          }
          catch {
            bitmap?.Dispose();
            throw;
          }

          // Feature 106 (research R-003): "no change" means the same PNG bytes as the current frame.
          // The first frame of a loop is always a change.
          var previous = CurrentFrame;
          var changed = previous is null || !previous.PngBytes.AsSpan().SequenceEqual(png);
          var frame = new CachedFrame(png, bitmap, DateTimeOffset.UtcNow, bitmap.Width, bitmap.Height);
          var oldFrame = Interlocked.Exchange(ref _currentFrame, frame);
          oldFrame?.Bitmap.Dispose();
          Interlocked.Increment(ref _frameCount);
          _tracker?.RecordCapture(_sessionId, changed);
        }
      }
      catch (OperationCanceledException) when (ct.IsCancellationRequested) {
        _gate.Failed(_deviceSerial);
        break;
      }
      catch (OperationCanceledException ex) {
        // The capture time limit fired, not the loop token: a failed capture. The loop continues.
        // Feature 121: the device process can still run, so the device is Suspect.
        _gate.TimedOut(_deviceSerial);
        BackgroundCaptureLog.CaptureTimedOut(_logger, _sessionId, _captureTimeoutMs, ex);
      }
      catch (Exception ex) {
        _gate.Failed(_deviceSerial);
        BackgroundCaptureLog.CaptureError(_logger, _sessionId, ex);
      }

      sw.Stop();
      RecordCaptureDuration(sw.ElapsedMilliseconds);

      // Delay remainder of interval (no overlapping captures)
      var elapsed = (int)sw.ElapsedMilliseconds;
      var delay = Math.Max(0, _intervalMs - elapsed);
      if (delay > 0 && !ct.IsCancellationRequested) {
        try { await Task.Delay(delay, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { break; }
      }
    }
  }

  /// <summary>
  /// Waits while the gate refuses a capture. For a Suspect device it waits one check interval, then asks
  /// the device if the <c>screencap</c> process ended. No answer, or a running process, keeps the state.
  /// </summary>
  private async Task WaitWhileBlockedAsync(CancellationToken ct) {
    try {
      if (!_gate.IsSuspect(_deviceSerial)) {
        await Task.Delay(Math.Max(50, _intervalMs), ct).ConfigureAwait(false);
        return;
      }
      await Task.Delay(_suspectCheckIntervalMs, ct).ConfigureAwait(false);
      if (!_gate.IsSuspect(_deviceSerial)) return;
      _gate.RecordCheck(_deviceSerial);
      var running = await _provider.HasRunningScreencapAsync(_transportCheckTimeoutMs, ct).ConfigureAwait(false);
      if (running == false) {
        _gate.Clear(_deviceSerial);
        BackgroundCaptureLog.SuspectCleared(_logger, _sessionId, _deviceSerial);
      }
    }
    catch (OperationCanceledException) {
      // The loop stops. The while condition ends the loop.
    }
    catch (Exception ex) {
      BackgroundCaptureLog.CaptureError(_logger, _sessionId, ex);
    }
  }

  private void RecordCaptureDuration(long durationMs) {
    lock (_metricsLock) {
      _captureDurationsMs[_rollingIndex] = durationMs;
      _rollingIndex = (_rollingIndex + 1) % RollingWindowSize;
      if (_rollingCount < RollingWindowSize) _rollingCount++;
    }
  }

  private double? ComputeFps() {
    if (_rollingCount == 0) return null;
    long total = 0;
    for (var i = 0; i < _rollingCount; i++) total += _captureDurationsMs[i];
    // When capture is near-instant, avgDuration can be 0; use configured interval as floor
    var avgDuration = _rollingCount > 0 ? (double)total / _rollingCount : 0;
    var effectiveInterval = Math.Max(avgDuration, _intervalMs);
    return 1000.0 / effectiveInterval;
  }
}

internal static partial class BackgroundCaptureLog {
  [LoggerMessage(EventId = 5001, Level = LogLevel.Information, Message = "Background capture loop started for session {SessionId} device {DeviceSerial} interval {IntervalMs}ms")]
  public static partial void LoopStarted(ILogger logger, string sessionId, string deviceSerial, int intervalMs);

  [LoggerMessage(EventId = 5002, Level = LogLevel.Information, Message = "Background capture loop stopped for session {SessionId}")]
  public static partial void LoopStopped(ILogger logger, string sessionId);

  [LoggerMessage(EventId = 5003, Level = LogLevel.Debug, Message = "Background capture loop restarted for session {SessionId}")]
  public static partial void LoopRestarted(ILogger logger, string sessionId);

  [LoggerMessage(EventId = 5004, Level = LogLevel.Debug, Message = "Background capture failed for session {SessionId}")]
  public static partial void CaptureError(ILogger logger, string sessionId, Exception ex);

  [LoggerMessage(EventId = 5005, Level = LogLevel.Debug, Message = "Background capture for session {SessionId} did not complete in {TimeoutMs} ms. The loop continues.")]
  public static partial void CaptureTimedOut(ILogger logger, string sessionId, int timeoutMs, Exception ex);

  [LoggerMessage(EventId = 5006, Level = LogLevel.Information, Message = "Background capture for session {SessionId}: the screencap process of device {DeviceSerial} ended. Captures start again.")]
  public static partial void SuspectCleared(ILogger logger, string sessionId, string deviceSerial);
}
