using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Domain.Triggers.Evaluators;
using GameBot.Emulator.Session;
using GameBot.Service.Endpoints;
using GameBot.Service.Endpoints.Dto;
using GameBot.Service.Services;
using Xunit;

namespace GameBot.Tests.Unit;

/// <summary>
/// Feature 085 / issue #176: <c>POST /api/images/detect</c> used to answer an unresolvable screen
/// with an empty match array — a fabricated "absent" indistinguishable from a real one, which
/// silently disarmed absence probes as soon as a second emulator was running. These tests pin the
/// resolution outcomes so that regression cannot return unnoticed.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ImageDetectTargetResolutionTests {
  private static byte[] CreateMinimalPng(int w = 8, int h = 8) {
    using var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb);
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
  }

  private static EmulatorSession RunningSession(string id) => new() {
    Id = id,
    GameId = "game",
    Status = SessionStatus.Running,
    DeviceSerial = $"emulator-{id}"
  };

  // ---- No target named -------------------------------------------------------------------

  [Fact(DisplayName = "Several running sessions and no named target resolves as ambiguous, not as an empty result")]
  public void SeveralSessionsWithNoTargetIsAmbiguous() {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(null),
      Sessions = new StubSessionManager(RunningSession("a"), RunningSession("b"))
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(new DetectRequest(), new CaptureSessionStore(), sp);

    outcome.Png.Should().BeNull();
    outcome.Status.Should().Be(StatusCodes409);
    outcome.Code.Should().Be("ambiguous_session");
    outcome.Message.Should().Contain("2 device sessions are active");
  }

  [Fact(DisplayName = "No screen and no running session resolves as unavailable")]
  public void NoScreenAndNoSessionsIsUnavailable() {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(null),
      Sessions = new StubSessionManager()
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(new DetectRequest(), new CaptureSessionStore(), sp);

    outcome.Png.Should().BeNull();
    outcome.Status.Should().Be(StatusCodes503);
    outcome.Code.Should().Be("emulator_unavailable");
  }

  [Fact(DisplayName = "No screen source registered at all resolves as unavailable, not as an empty result")]
  public void MissingScreenSourceIsUnavailable() {
    var sp = new StubServiceProvider { ScreenSource = null, Sessions = new StubSessionManager() };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(new DetectRequest(), new CaptureSessionStore(), sp);

    outcome.Png.Should().BeNull();
    outcome.Status.Should().Be(StatusCodes503);
    outcome.Code.Should().Be("emulator_unavailable");
  }

  [Fact(DisplayName = "A screen that resolves is measured even with zero running sessions (stub hosts)")]
  public void ResolvedScreenWithZeroSessionsStillMeasures() {
    // Guards FR-016. Stub hosts serve a fixed bitmap with no sessions at all, so an implementation
    // that counted sessions before asking for a frame would break every existing contract test.
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(new Bitmap(4, 4, PixelFormat.Format24bppRgb)),
      Sessions = new StubSessionManager()
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(new DetectRequest(), new CaptureSessionStore(), sp);

    outcome.Png.Should().NotBeNull();
    outcome.Code.Should().BeNull();
  }

  // ---- captureId -------------------------------------------------------------------------

  [Fact(DisplayName = "A known captureId is measured even while several sessions are running")]
  public void KnownCaptureIdWinsOverAmbiguity() {
    var captures = new CaptureSessionStore();
    var stored = captures.Add(CreateMinimalPng());
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(null),
      Sessions = new StubSessionManager(RunningSession("a"), RunningSession("b"))
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(
      new DetectRequest { CaptureId = stored.Id }, captures, sp);

    outcome.Png.Should().Equal(stored.Png);
    outcome.Code.Should().BeNull();
  }

  [Fact(DisplayName = "An unknown or trimmed captureId is a not-found, never a fallback to another screen")]
  public void UnknownCaptureIdIsNotFound() {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(new Bitmap(4, 4, PixelFormat.Format24bppRgb)),
      Sessions = new StubSessionManager(RunningSession("a"))
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(
      new DetectRequest { CaptureId = "no-such-capture" }, new CaptureSessionStore(), sp);

    // A usable screen exists, but the caller named a specific one: substituting it would be exactly
    // the silent-wrong-answer behaviour this feature removes.
    outcome.Png.Should().BeNull();
    outcome.Status.Should().Be(StatusCodes404);
    outcome.Code.Should().Be("capture_not_found");
  }

  // ---- sessionId -------------------------------------------------------------------------

  [Fact(DisplayName = "An unknown sessionId is a not-found")]
  public void UnknownSessionIdIsNotFound() {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(new Bitmap(4, 4, PixelFormat.Format24bppRgb)),
      Sessions = new StubSessionManager()
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(
      new DetectRequest { SessionId = "ghost" }, new CaptureSessionStore(), sp);

    outcome.Png.Should().BeNull();
    outcome.Status.Should().Be(StatusCodes404);
    outcome.Code.Should().Be("session_not_found");
  }

  [Fact(DisplayName = "A known session with no captured frame yet is unavailable, not an empty result")]
  public void KnownSessionWithoutFrameIsUnavailable() {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(null),
      Sessions = new StubSessionManager(RunningSession("a")),
      ScreenSourceFactory = new StubScreenSourceFactory(new StubScreenSource(null))
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(
      new DetectRequest { SessionId = "a" }, new CaptureSessionStore(), sp);

    outcome.Png.Should().BeNull();
    outcome.Status.Should().Be(StatusCodes503);
    outcome.Code.Should().Be("emulator_unavailable");
  }

  [Fact(DisplayName = "A named session is measured even while several sessions are running")]
  public void NamedSessionWinsOverAmbiguity() {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(null),
      Sessions = new StubSessionManager(RunningSession("a"), RunningSession("b")),
      ScreenSourceFactory = new StubScreenSourceFactory(
        new StubScreenSource(new Bitmap(4, 4, PixelFormat.Format24bppRgb)))
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(
      new DetectRequest { SessionId = "a" }, new CaptureSessionStore(), sp);

    outcome.Png.Should().NotBeNull();
    outcome.Code.Should().BeNull();
  }

  // ---- blank values ----------------------------------------------------------------------

  [Theory(DisplayName = "Blank target values count as absent, not as malformed")]
  [InlineData("")]
  [InlineData("   ")]
  public void BlankTargetsFallBackToImplicitResolution(string blank) {
    var sp = new StubServiceProvider {
      ScreenSource = new StubScreenSource(new Bitmap(4, 4, PixelFormat.Format24bppRgb)),
      Sessions = new StubSessionManager()
    };

    var outcome = ImageDetectionsEndpoints.ResolveFrame(
      new DetectRequest { CaptureId = blank, SessionId = blank }, new CaptureSessionStore(), sp);

    outcome.Png.Should().NotBeNull("a blank target must not divert a request off the path it used before");
    outcome.Code.Should().BeNull();
  }

  private const int StatusCodes404 = 404;
  private const int StatusCodes409 = 409;
  private const int StatusCodes503 = 503;

  // ---- stubs -----------------------------------------------------------------------------

  private sealed class StubScreenSource : IScreenSource {
    private readonly Bitmap? _bmp;
    public StubScreenSource(Bitmap? bmp) => _bmp = bmp;
    // ResolveFrame disposes what it is handed, so return a copy each call.
    public Bitmap? GetLatestScreenshot() => _bmp is null ? null : new Bitmap(_bmp);
  }

  private sealed class StubScreenSourceFactory : IScreenSourceFactory {
    private readonly IScreenSource _inner;
    public StubScreenSourceFactory(IScreenSource inner) => _inner = inner;
    public IScreenSource ForSession(string sessionId) => _inner;
  }

  private sealed class StubSessionManager : ISessionManager {
    private readonly List<EmulatorSession> _sessions;
    public StubSessionManager(params EmulatorSession[] sessions) => _sessions = sessions.ToList();

    public int ActiveCount => _sessions.Count;
    public bool CanCreateSession => true;
    public EmulatorSession? GetSession(string id) => _sessions.Find(s => s.Id == id);
    public IReadOnlyCollection<EmulatorSession> ListSessions() => _sessions;

    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) =>
      throw new NotSupportedException();
    public bool StopSession(string id) => throw new NotSupportedException();
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
      throw new NotSupportedException();
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
      throw new NotSupportedException();
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) =>
      throw new NotSupportedException();
  }

  private sealed class StubServiceProvider : IServiceProvider {
    public IScreenSource? ScreenSource { get; init; }
    public IScreenSourceFactory? ScreenSourceFactory { get; init; }
    public ISessionManager? Sessions { get; init; }

    public object? GetService(Type serviceType) {
      if (serviceType == typeof(IScreenSource)) return ScreenSource;
      if (serviceType == typeof(IScreenSourceFactory)) return ScreenSourceFactory;
      if (serviceType == typeof(ISessionManager)) return Sessions;
      return null;
    }
  }
}
