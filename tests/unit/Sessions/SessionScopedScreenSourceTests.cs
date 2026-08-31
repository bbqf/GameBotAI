using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sessions;

/// <summary>
/// Feature 079: a session-scoped screen source observes its own device and nothing else.
///
/// The defect it replaces: <c>BackgroundCaptureScreenSource</c> answered every screen question with
/// the frame of the <i>first</i> running session, so with two queue runs active one run's image and
/// OCR checks silently evaluated the other run's screen.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionScopedScreenSourceTests : IDisposable {
  // Distinct sizes make "whose frame is this?" unambiguous without pixel comparison.
  private const int SessionAWidth = 7;
  private const int SessionBWidth = 13;

  private readonly BackgroundScreenCaptureService _capture;

  public SessionScopedScreenSourceTests() {
    _capture = new BackgroundScreenCaptureService(
      serial => new SolidColorCaptureProvider(WidthForSerial(serial)),
      captureIntervalMs: 50,
      NullLogger<BackgroundScreenCaptureService>.Instance);
  }

  public void Dispose() => _capture.Dispose();

  [Fact]
  public async Task EachSessionObservesOnlyItsOwnDevice() {
    _capture.StartCapture("session-a", "serial-a");
    _capture.StartCapture("session-b", "serial-b");
    await WaitForFrameAsync("session-a").ConfigureAwait(false);
    await WaitForFrameAsync("session-b").ConfigureAwait(false);

    var a = new SessionScopedScreenSource(_capture, "session-a");
    var b = new SessionScopedScreenSource(_capture, "session-b");

    using var frameA = a.GetLatestScreenshot();
    using var frameB = b.GetLatestScreenshot();

    frameA!.Width.Should().Be(SessionAWidth);
    frameB!.Width.Should().Be(SessionBWidth);
  }

  [Fact]
  public void AnUnknownSessionYieldsNoFrameRatherThanAnotherSessions() {
    _capture.StartCapture("session-a", "serial-a");

    var orphan = new SessionScopedScreenSource(_capture, "session-does-not-exist");

    orphan.GetLatestScreenshot().Should().BeNull();
  }

  [Fact]
  public async Task AStoppedSessionYieldsNoFrame() {
    _capture.StartCapture("session-a", "serial-a");
    await WaitForFrameAsync("session-a").ConfigureAwait(false);
    var source = new SessionScopedScreenSource(_capture, "session-a");
    source.GetLatestScreenshot().Should().NotBeNull();

    _capture.StopCapture("session-a");

    source.GetLatestScreenshot().Should().BeNull();
  }

  [Fact]
  public async Task TheFactoryBindsEachSourceToItsOwnSession() {
    _capture.StartCapture("session-a", "serial-a");
    _capture.StartCapture("session-b", "serial-b");
    await WaitForFrameAsync("session-a").ConfigureAwait(false);
    await WaitForFrameAsync("session-b").ConfigureAwait(false);

    var factory = new BackgroundCaptureScreenSourceFactory(_capture);

    using var frameA = factory.ForSession("session-a").GetLatestScreenshot();
    using var frameB = factory.ForSession("session-b").GetLatestScreenshot();

    frameA!.Width.Should().Be(SessionAWidth);
    frameB!.Width.Should().Be(SessionBWidth);
  }

  [Fact]
  public void ABlankSessionIdIsRejected() {
    var factory = new BackgroundCaptureScreenSourceFactory(_capture);
    var act = () => factory.ForSession("   ");
    act.Should().Throw<ArgumentException>();
  }

  /// <summary>Polls until the session's capture loop has produced its first frame.</summary>
  private async Task WaitForFrameAsync(string sessionId) {
    var sw = Stopwatch.StartNew();
    while (sw.Elapsed < TimeSpan.FromSeconds(5)) {
      if (_capture.GetCachedFrame(sessionId) is not null) return;
      await Task.Delay(20).ConfigureAwait(false);
    }
    throw new TimeoutException($"No frame captured for '{sessionId}' within 5s.");
  }

  private static int WidthForSerial(string serial) =>
    string.Equals(serial, "serial-a", StringComparison.Ordinal) ? SessionAWidth : SessionBWidth;
}

/// <summary>Capture provider returning a PNG of a fixed width, so frames are attributable by size.</summary>
[SupportedOSPlatform("windows")]
file sealed class SolidColorCaptureProvider : IAdbScreenCaptureProvider {
  private readonly byte[] _png;

  public SolidColorCaptureProvider(int width) {
    using var bmp = new Bitmap(width, 4);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    _png = ms.ToArray();
  }

  public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(_png);
}
