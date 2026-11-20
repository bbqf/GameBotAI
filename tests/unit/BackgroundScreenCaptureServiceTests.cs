#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests;

public sealed class BackgroundScreenCaptureServiceTests : IDisposable {
  private readonly BackgroundScreenCaptureService _service;

  public BackgroundScreenCaptureServiceTests() {
    _service = new BackgroundScreenCaptureService(
        serial => new FakeCaptureProvider(),
        100, // 100ms interval for fast tests
        NullLogger<BackgroundScreenCaptureService>.Instance);
  }

  public void Dispose() => _service.Dispose();

  [Fact]
  public async Task StartCaptureCreatesLoopAndCapturesFrame() {
    _service.StartCapture("sess-1", "device-1");

    // Wait for at least one capture cycle
    await Task.Delay(300);

    var frame = _service.GetCachedFrame("sess-1");
    frame.Should().NotBeNull();
    frame!.PngBytes.Should().NotBeEmpty();
    frame.Width.Should().BeGreaterThan(0);
    frame.Height.Should().BeGreaterThan(0);
  }

  [Fact]
  public void GetCachedFrameReturnsNullBeforeFirstCapture() {
    // No loop started
    _service.GetCachedFrame("nonexistent").Should().BeNull();
  }

  [Fact]
  public async Task StopCaptureCancelsLoopAndDisposesResources() {
    _service.StartCapture("sess-1", "device-1");
    await Task.Delay(300);

    _service.StopCapture("sess-1");

    _service.GetCachedFrame("sess-1").Should().BeNull();
    _service.GetCaptureMetrics("sess-1").Should().BeNull();
  }

  [Fact]
  public async Task GetCaptureMetricsReturnsRollingFps() {
    _service.StartCapture("sess-1", "device-1");
    await Task.Delay(800); // Allow several capture cycles at 100ms interval

    var metrics = _service.GetCaptureMetrics("sess-1");
    metrics.Should().NotBeNull();
    metrics!.FrameCount.Should().BeGreaterThan(0);
    metrics.CaptureRateFps.Should().NotBeNull().And.BeGreaterThan(0);
    metrics.LastCaptureUtc.Should().NotBeNull();
  }

  [Fact]
  public void GetCaptureMetricsReturnsNullForUnknownSession() {
    _service.GetCaptureMetrics("nonexistent").Should().BeNull();
  }

  [Fact]
  public async Task ConcurrentReadsDoNotBlock() {
    _service.StartCapture("sess-1", "device-1");
    await Task.Delay(300);

    var sw = Stopwatch.StartNew();
    var tasks = new Task[10];
    for (var i = 0; i < tasks.Length; i++) {
      tasks[i] = Task.Run(() => _service.GetCachedFrame("sess-1"));
    }
    await Task.WhenAll(tasks);
    sw.Stop();

    sw.ElapsedMilliseconds.Should().BeLessThan(100, "concurrent reads should complete instantly");
  }

  [Fact]
  public async Task StopAllDisposesAllLoops() {
    _service.StartCapture("sess-1", "device-1");
    _service.StartCapture("sess-2", "device-2");
    await Task.Delay(300);

    _service.StopAll();

    _service.GetCachedFrame("sess-1").Should().BeNull();
    _service.GetCachedFrame("sess-2").Should().BeNull();
  }

  [Fact]
  public async Task DuplicateStartCaptureStopsOldLoopFirst() {
    _service.StartCapture("sess-1", "device-1");
    await Task.Delay(300);

    var firstFrame = _service.GetCachedFrame("sess-1");
    firstFrame.Should().NotBeNull();

    // Restart with a different device
    _service.StartCapture("sess-1", "device-2");
    await Task.Delay(300);

    var secondFrame = _service.GetCachedFrame("sess-1");
    secondFrame.Should().NotBeNull();
  }

  [Fact]
  public async Task GetCachedFrameCompletesUnder5Ms() {
    _service.StartCapture("sess-1", "device-1");
    await Task.Delay(300);

    var sw = Stopwatch.StartNew();
    _ = _service.GetCachedFrame("sess-1");
    sw.Stop();

    sw.ElapsedMilliseconds.Should().BeLessThan(5, "SC-001: cached frame retrieval must be near-instant");
  }

  [Fact]
  public async Task FailingProviderContinuesCapturing() {
    var provider = new FailThenSucceedProvider(failCount: 2);
    using var service = new BackgroundScreenCaptureService(
        _ => provider,
        50,
        NullLogger<BackgroundScreenCaptureService>.Instance);

    service.StartCapture("sess-1", "device-1");
    await Task.Delay(500);

    var frame = service.GetCachedFrame("sess-1");
    frame.Should().NotBeNull("capture should succeed after transient failures");
  }

  [Fact]
  public void StopCaptureNoOpForUnknownSession() {
    // Should not throw
    _service.StopCapture("nonexistent");
  }

  // ── Feature 106: capture data for the liveness tracker ─────────────────────

  [Fact]
  public async Task SameBytesTwoTimesGiveNoChangeOnTheSecondCapture() {
    var tracker = new RecordingLivenessTracker();
    using var service = new BackgroundScreenCaptureService(_ => new FakeCaptureProvider(), 50,
      NullLogger<BackgroundScreenCaptureService>.Instance, tracker);

    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => tracker.Captures.Count >= 2);

    var captures = tracker.Captures.ToArray();
    captures[0].Should().Be(("sess-1", true), "the first frame of a loop is always a change");
    captures[1].Should().Be(("sess-1", false), "the same PNG bytes are no change");
  }

  [Fact]
  public async Task NewBytesGiveAChange() {
    var tracker = new RecordingLivenessTracker();
    using var service = new BackgroundScreenCaptureService(_ => new ChangingCaptureProvider(), 50,
      NullLogger<BackgroundScreenCaptureService>.Instance, tracker);

    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => tracker.Captures.Count >= 3);

    tracker.Captures.Take(3).Should().OnlyContain(c => c.Changed);
  }

  [Fact]
  public async Task AHungCaptureDoesNotStopTheLoop() {
    var tracker = new RecordingLivenessTracker();
    var provider = new HangOnceProvider();
    using var service = new BackgroundScreenCaptureService(_ => provider, 50,
      NullLogger<BackgroundScreenCaptureService>.Instance, tracker,
      new DeviceLivenessOptions { CaptureTimeoutMs = 200, CaptureStallLimitMs = 1000 });

    service.StartCapture("sess-1", "device-1");
    // Feature 121: after the time-out the loop waits one device check (CaptureStallLimitMs, minimum 1 s).
    await WaitForAsync(() => !tracker.Captures.IsEmpty, timeoutMs: 8000);

    provider.Calls.Should().BeGreaterThanOrEqualTo(2, "the capture after the hung one ran");
    provider.HungCallWasCancelled.Should().BeTrue("the capture time limit cancelled the hung capture");
    tracker.Captures.Should().HaveCount(1, "the tracker records no capture for the hung one");
    service.GetCachedFrame("sess-1").Should().NotBeNull();
  }

  [Fact]
  public void StartAndStopCaptureTellTheTracker() {
    var tracker = new RecordingLivenessTracker();
    using var service = new BackgroundScreenCaptureService(_ => new FakeCaptureProvider(), 50,
      NullLogger<BackgroundScreenCaptureService>.Instance, tracker);

    service.StartCapture("sess-1", "device-1");
    service.StartCapture("sess-2", "device-2");
    service.StopCapture("sess-1");
    service.StopAll();

    tracker.Started.Should().Equal("sess-1", "sess-2");
    tracker.Stopped.Should().Equal("sess-1", "sess-2");
  }

  // ── Feature 197: frame id and stale repeat ─────────────────────────────────

  private static BackgroundScreenCaptureService StaleRepeatService(IAdbScreenCaptureProvider provider, IDeviceLivenessTracker? tracker = null, int frameMaxAgeMs = 60000) =>
    new(_ => provider, 50, NullLogger<BackgroundScreenCaptureService>.Instance, tracker,
      new DeviceLivenessOptions { CaptureTimeoutMs = 200, CaptureStallLimitMs = 1000, FrameMaxAgeMs = frameMaxAgeMs });

  [Fact]
  public async Task ANewFrameHasANewFrameId() {
    using var service = new BackgroundScreenCaptureService(_ => new ChangingCaptureProvider(), 50,
      NullLogger<BackgroundScreenCaptureService>.Instance);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1") is not null);
    var first = service.GetCachedFrame("sess-1")!.FrameId;

    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.FrameId != first);

    service.GetCachedFrame("sess-1")!.FrameId.Should().NotBe(first);
  }

  [Fact]
  public async Task SameBytesWithNoFailureGiveANewFrameAndNoFlag() {
    // A still screen: the device answers, and the bytes do not change.
    using var service = new BackgroundScreenCaptureService(_ => new FakeCaptureProvider(), 50,
      NullLogger<BackgroundScreenCaptureService>.Instance);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1") is not null);
    var first = service.GetCachedFrame("sess-1")!.FrameId;

    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.FrameId != first);

    var frame = service.GetCachedFrame("sess-1")!;
    frame.FrameId.Should().NotBe(first);
    frame.StaleRepeat.Should().BeFalse();
  }

  [Fact]
  public async Task SameBytesAfterATimeoutAreAStaleRepeat() {
    var tracker = new RecordingLivenessTracker();
    var provider = new TimeoutThenRepeatProvider();
    using var service = StaleRepeatService(provider, tracker);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1") is not null);
    var first = service.GetCachedFrame("sess-1")!;

    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == true, timeoutMs: 8000);
    // Some more repeats.
    var calls = provider.Calls;
    await WaitForAsync(() => provider.Calls >= calls + 3);

    var frame = service.GetCachedFrame("sess-1")!;
    frame.StaleRepeat.Should().BeTrue();
    frame.StaleRepeatSince.Should().NotBeNull();
    frame.FrameId.Should().Be(first.FrameId, "a stale repeat keeps the frame id");
    frame.Timestamp.Should().Be(first.Timestamp, "a stale repeat keeps the capture time");
    tracker.Captures.Should().HaveCount(1, "a stale repeat is not a completed capture");
    frame.Bitmap.Width.Should().BeGreaterThan(0, "the shared bitmap of a stale repeat is not disposed");
  }

  [Fact]
  public async Task DifferentBytesEndTheStaleRepeat() {
    var provider = new TimeoutThenRepeatProvider();
    using var service = StaleRepeatService(provider);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == true, timeoutMs: 8000);
    var stale = service.GetCachedFrame("sess-1")!;

    provider.UseOtherBytes();
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == false);

    var frame = service.GetCachedFrame("sess-1")!;
    frame.StaleRepeat.Should().BeFalse();
    frame.StaleRepeatSince.Should().BeNull();
    frame.FrameId.Should().NotBe(stale.FrameId);
  }

  [Fact]
  public async Task RepeatsLongerThanTheFrameLimitGiveANewFrame() {
    var tracker = new RecordingLivenessTracker();
    var provider = new TimeoutThenRepeatProvider();
    using var service = StaleRepeatService(provider, tracker, frameMaxAgeMs: 1000);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == true, timeoutMs: 8000);
    var stale = service.GetCachedFrame("sess-1")!;

    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == false, timeoutMs: 4000);

    var frame = service.GetCachedFrame("sess-1")!;
    frame.StaleRepeat.Should().BeFalse("a still screen ends the repeat run after the frame limit");
    frame.FrameId.Should().NotBe(stale.FrameId);
    tracker.Captures.Should().HaveCountGreaterThanOrEqualTo(2);
    tracker.Captures.Skip(1).First().Changed.Should().BeFalse();
  }

  [Fact]
  public async Task ARestartOfTheLoopStartsWithNoStaleRepeatState() {
    var provider = new TimeoutThenRepeatProvider();
    using var service = StaleRepeatService(provider);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == true, timeoutMs: 8000);

    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1") is not null);
    var first = service.GetCachedFrame("sess-1")!;
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.FrameId != first.FrameId);

    var second = service.GetCachedFrame("sess-1")!;
    first.StaleRepeat.Should().BeFalse();
    second.StaleRepeat.Should().BeFalse("the same bytes with no failure in the new loop are a normal new frame");
    second.FrameId.Should().NotBe(first.FrameId);
  }

  [Fact]
  public async Task StaleRepeatsForLongerThanTheStallLimitGiveCaptureStalled() {
    // Feature 197 (FR-011, SC-005): FrameMaxAgeMs is above the stall limit for this test.
    var clock = new GameBot.UnitTests.Queues.FakeTimeProvider(new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero));
    var tracker = new DeviceLivenessTracker(clock);
    var provider = new TimeoutThenRepeatProvider();
    var options = new DeviceLivenessOptions { CaptureTimeoutMs = 200, CaptureStallLimitMs = 1000, FrameMaxAgeMs = 60000 };
    using var service = new BackgroundScreenCaptureService(_ => provider, 50,
      NullLogger<BackgroundScreenCaptureService>.Instance, tracker, options);
    service.StartCapture("sess-1", "device-1");
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == true, timeoutMs: 8000);

    clock.Advance(TimeSpan.FromMilliseconds(2000));
    var calls = provider.Calls;
    await WaitForAsync(() => provider.Calls >= calls + 3);
    var stalled = DeviceLivenessEvaluator.Evaluate(tracker.Sample("sess-1", hasDevice: true), options.Normalized(), clock.GetUtcNow());

    stalled.State.Should().Be(DeviceLivenessStates.NotLive);
    stalled.Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);

    provider.UseOtherBytes();
    await WaitForAsync(() => service.GetCachedFrame("sess-1")?.StaleRepeat == false);
    var live = DeviceLivenessEvaluator.Evaluate(tracker.Sample("sess-1", hasDevice: true), options.Normalized(), clock.GetUtcNow());

    live.State.Should().Be(DeviceLivenessStates.Live);
  }

  private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 3000) {
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(10);
  }
}

file sealed class RecordingLivenessTracker : IDeviceLivenessTracker {
  public System.Collections.Concurrent.ConcurrentQueue<(string SessionId, bool Changed)> Captures { get; } = new();
  public System.Collections.Generic.List<string> Started { get; } = new();
  public System.Collections.Generic.List<string> Stopped { get; } = new();
  public DateTimeOffset Now => DateTimeOffset.UtcNow;
  public void LoopStarted(string sessionId) { lock (Started) Started.Add(sessionId); }
  public void LoopStopped(string sessionId) { lock (Stopped) Stopped.Add(sessionId); }
  public void RecordCapture(string sessionId, bool changed) => Captures.Enqueue((sessionId, changed));
  public void RecordInputStarted(string sessionId) { }
  public void RecordInputCompleted(string sessionId, InputOutcome outcome) { }
  public void Remove(string sessionId) { }
  public DeviceLivenessSample Sample(string sessionId, bool hasDevice, bool? transportReady = null) =>
    new(hasDevice, transportReady, false, null, null, null, null, null, null);
  public bool HasCaptureData(string sessionId) => false;
}

file sealed class ChangingCaptureProvider : IAdbScreenCaptureProvider {
  private int _calls;

  public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
    var n = Interlocked.Increment(ref _calls);
    using var bmp = new System.Drawing.Bitmap(2, 2);
    bmp.SetPixel(0, 0, System.Drawing.Color.FromArgb(255, n % 256, 0, 0));
    using var ms = new System.IO.MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return Task.FromResult<byte[]?>(ms.ToArray());
  }
}

/// <summary>The first capture hangs until its token is cancelled. The captures after it succeed.</summary>
file sealed class HangOnceProvider : IAdbScreenCaptureProvider {
  private int _calls;
  public int Calls => Volatile.Read(ref _calls);
  public bool HungCallWasCancelled { get; private set; }

  // Feature 121: a timed-out capture makes the device Suspect. The device check says that no screencap
  // process runs, so the loop starts the next capture.
  public Task<bool?> HasRunningScreencapAsync(int timeoutMs, CancellationToken ct) => Task.FromResult<bool?>(false);

  public async Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
    if (Interlocked.Increment(ref _calls) == 1) {
      try {
        await Task.Delay(Timeout.Infinite, ct);
      }
      catch (OperationCanceledException) {
        HungCallWasCancelled = true;
        throw;
      }
    }
    using var bmp = new System.Drawing.Bitmap(2, 2);
    using var ms = new System.IO.MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return ms.ToArray();
  }
}

/// <summary>
/// Feature 197: the first capture returns frame A, the second capture hangs until its time limit, and the
/// captures after it return frame A again (a stale repeat). <see cref="UseOtherBytes"/> switches to frame B.
/// </summary>
file sealed class TimeoutThenRepeatProvider : IAdbScreenCaptureProvider {
  private static readonly byte[] FrameA = Png(System.Drawing.Color.Red);
  private static readonly byte[] FrameB = Png(System.Drawing.Color.Blue);
  private int _calls;
  private volatile bool _useB;

  public int Calls => Volatile.Read(ref _calls);

  public void UseOtherBytes() => _useB = true;

  public Task<bool?> HasRunningScreencapAsync(int timeoutMs, CancellationToken ct) => Task.FromResult<bool?>(false);

  public async Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
    if (Interlocked.Increment(ref _calls) == 2) {
      await Task.Delay(Timeout.Infinite, ct);
    }
    return _useB ? FrameB : FrameA;
  }

  private static byte[] Png(System.Drawing.Color color) {
    using var bmp = new System.Drawing.Bitmap(2, 2);
    bmp.SetPixel(0, 0, color);
    using var ms = new System.IO.MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return ms.ToArray();
  }
}

file sealed class FakeCaptureProvider : IAdbScreenCaptureProvider {
  // 1x1 red PNG
  private static readonly byte[] FakePng = CreateMinimalPng();

  public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
    return Task.FromResult<byte[]?>(FakePng);
  }

  private static byte[] CreateMinimalPng() {
    // Create a tiny 1x1 Bitmap and encode as PNG
    using var bmp = new System.Drawing.Bitmap(2, 2);
    using var ms = new System.IO.MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return ms.ToArray();
  }
}

file sealed class FailThenSucceedProvider : IAdbScreenCaptureProvider {
  private int _callCount;
  private readonly int _failCount;

  public FailThenSucceedProvider(int failCount) => _failCount = failCount;

  public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
    if (Interlocked.Increment(ref _callCount) <= _failCount)
      throw new InvalidOperationException("Simulated ADB failure");

    using var bmp = new System.Drawing.Bitmap(2, 2);
    using var ms = new System.IO.MemoryStream();
    bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
    return Task.FromResult<byte[]?>(ms.ToArray());
  }
}
