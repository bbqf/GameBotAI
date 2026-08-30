using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sessions;

/// <summary>
/// Feature 079, the core defect and its fix.
///
/// <c>BackgroundCaptureScreenSource</c> used to answer every "what is on screen" question with the
/// frame of the <i>first</i> running session it found. With two queue runs active, one run's image and
/// OCR checks therefore evaluated the other run's screen — non-deterministically, and silently, so the
/// run kept going and tapped the right coordinates on the wrong game.
///
/// It now resolves: ambient device context → the single running session → nothing.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class AmbientScreenIsolationTests : IDisposable {
  private const int WidthA = 7;
  private const int WidthB = 13;

  private readonly BackgroundScreenCaptureService _capture;
  private readonly TwoSessionManager _sessions = new();
  private readonly AsyncLocalDeviceContextAccessor _context = new();

  public AmbientScreenIsolationTests() {
    _capture = new BackgroundScreenCaptureService(
      serial => new SizedCaptureProvider(string.Equals(serial, "serial-a", StringComparison.Ordinal) ? WidthA : WidthB),
      captureIntervalMs: 50,
      NullLogger<BackgroundScreenCaptureService>.Instance);
  }

  public void Dispose() => _capture.Dispose();

  [Fact]
  public async Task ConcurrentFlowsEachObserveTheirOwnDevice() {
    await StartBothSessionsAsync().ConfigureAwait(false);
    var screen = new BackgroundCaptureScreenSource(_capture, _sessions, _context);

    async Task<int?> ObserveAs(string sessionId) {
      using var scope = _context.Push(DeviceContext.For(sessionId));
      await Task.Delay(10).ConfigureAwait(false); // interleave the two flows
      using var frame = screen.GetLatestScreenshot();
      return frame?.Width;
    }

    var widths = await Task.WhenAll(ObserveAs("session-a"), ObserveAs("session-b")).ConfigureAwait(false);

    widths[0].Should().Be(WidthA);
    widths[1].Should().Be(WidthB);
  }

  [Fact]
  public async Task WithoutAContextAndSeveralSessionsNothingIsObserved() {
    await StartBothSessionsAsync().ConfigureAwait(false);
    var screen = new BackgroundCaptureScreenSource(_capture, _sessions, _context);

    // The pre-079 behaviour returned session A's frame here, silently.
    screen.GetLatestScreenshot().Should().BeNull();
  }

  [Fact]
  public async Task WithASingleSessionTheContextIsNotRequired() {
    _sessions.Add("session-a", "serial-a");
    _capture.StartCapture("session-a", "serial-a");
    await WaitForFrameAsync("session-a").ConfigureAwait(false);
    var screen = new BackgroundCaptureScreenSource(_capture, _sessions, _context);

    using var frame = screen.GetLatestScreenshot();

    frame!.Width.Should().Be(WidthA, "single-emulator behaviour must be unchanged");
  }

  [Fact]
  public async Task TheContextOverridesEvenTheSingleRunningSession() {
    await StartBothSessionsAsync().ConfigureAwait(false);
    _sessions.MarkStopped("session-a"); // only B is "running"
    var screen = new BackgroundCaptureScreenSource(_capture, _sessions, _context);

    using var scope = _context.Push(DeviceContext.For("session-a"));
    using var frame = screen.GetLatestScreenshot();

    frame!.Width.Should().Be(WidthA, "the flow's own device wins over the listing");
  }

  [Fact]
  public void WithNoSessionsAtAllNothingIsObserved() {
    var screen = new BackgroundCaptureScreenSource(_capture, _sessions, _context);
    screen.GetLatestScreenshot().Should().BeNull();
  }

  private async Task StartBothSessionsAsync() {
    _sessions.Add("session-a", "serial-a");
    _sessions.Add("session-b", "serial-b");
    _capture.StartCapture("session-a", "serial-a");
    _capture.StartCapture("session-b", "serial-b");
    await WaitForFrameAsync("session-a").ConfigureAwait(false);
    await WaitForFrameAsync("session-b").ConfigureAwait(false);
  }

  private async Task WaitForFrameAsync(string sessionId) {
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < TimeSpan.FromSeconds(5)) {
      if (_capture.GetCachedFrame(sessionId) is not null) return;
      await Task.Delay(20).ConfigureAwait(false);
    }
    throw new TimeoutException($"No frame captured for '{sessionId}' within 5s.");
  }
}

/// <summary>Session manager fake that can hold several running sessions at once.</summary>
internal sealed class TwoSessionManager : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  public void Add(string id, string serial) =>
    _sessions.Add(new EmulatorSession { Id = id, GameId = "g", Status = SessionStatus.Running, DeviceSerial = serial });

  public void MarkStopped(string id) {
    var s = _sessions.Find(x => x.Id == id);
    if (s is not null) s.Status = SessionStatus.Stopped;
  }

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => true;
  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => throw new NotSupportedException();
  public EmulatorSession? GetSession(string id) => _sessions.Find(s => s.Id == id);
  public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;
  public bool StopSession(string id) => _sessions.RemoveAll(s => s.Id == id) > 0;
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

/// <summary>Capture provider producing a PNG of a fixed width, so frames are attributable by size.</summary>
[SupportedOSPlatform("windows")]
file sealed class SizedCaptureProvider : IAdbScreenCaptureProvider {
  private readonly byte[] _png;

  public SizedCaptureProvider(int width) {
    using var bmp = new Bitmap(width, 4);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    _png = ms.ToArray();
  }

  public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(_png);
}
