using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sessions;

/// <summary>
/// Feature 166 (FR-013): each removal of a session stops its capture loop. The session manager raises
/// <see cref="ISessionRemovalNotifier.SessionRemoved"/>, and <see cref="SessionCaptureCleanup"/> stops
/// the loop.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionCaptureCleanupTests {
  private static SessionManager CreateManager(int idleTimeoutSeconds = 1800) =>
    new(
      Options.Create(new SessionOptions { MaxConcurrentSessions = 4, IdleTimeoutSeconds = idleTimeoutSeconds }),
      NullLogger<SessionManager>.Instance,
      NullLogger<GameBot.Emulator.Adb.AdbClient>.Instance,
      appConfig: null,
      liveness: null,
      livenessOptions: null,
      clientFactory: _ => throw new NotSupportedException("No device input in this test."));

  [Fact]
  public async Task IdleSweepStopsCaptureLoop() {
    var manager = CreateManager(idleTimeoutSeconds: 1);
    using var capture = new BackgroundScreenCaptureService(_ => new FixedPngProvider(), 50, NullLogger<BackgroundScreenCaptureService>.Instance);
    SessionCaptureCleanup.Attach(manager, capture);
    var session = manager.CreateSession("game-166", "emulator-test");
    capture.StartCapture(session.Id, "emulator-test");

    var sw = Stopwatch.StartNew();
    while (capture.GetCachedFrame(session.Id) is null && sw.ElapsedMilliseconds < 2000) {
      await Task.Delay(20);
    }
    capture.GetCachedFrame(session.Id).Should().NotBeNull();

    // No session manager call for more than the idle timeout. The sweep is lazy, so the next call removes the session.
    await Task.Delay(1300);
    manager.ListSessions().Should().NotContain(s => s.Id == session.Id);

    capture.GetCaptureMetrics(session.Id).Should().BeNull();
    capture.GetCachedFrame(session.Id).Should().BeNull();
  }

  [Fact]
  public void StopSessionRaisesSessionRemovedOnce() {
    var manager = CreateManager();
    var session = manager.CreateSession("game-166");
    var removed = new List<string>();
    ((ISessionRemovalNotifier)manager).SessionRemoved += (_, e) => removed.Add(e.SessionId);

    manager.StopSession(session.Id).Should().BeTrue();

    removed.Should().ContainSingle().Which.Should().Be(session.Id);
  }

  [Fact]
  public void StopSessionOfUnknownIdRaisesNoEvent() {
    var manager = CreateManager();
    var removed = new List<string>();
    ((ISessionRemovalNotifier)manager).SessionRemoved += (_, e) => removed.Add(e.SessionId);

    manager.StopSession("no-such-session").Should().BeFalse();

    removed.Should().BeEmpty();
  }

  [Fact]
  public void HandlerExceptionDoesNotFailRemoval() {
    var manager = CreateManager();
    var session = manager.CreateSession("game-166");
    ((ISessionRemovalNotifier)manager).SessionRemoved += (_, _) => throw new InvalidOperationException("handler failure");

    var act = () => manager.StopSession(session.Id);

    act.Should().NotThrow();
    manager.GetSession(session.Id).Should().BeNull();
  }

  [Fact]
  public void AttachWithManagerThatIsNotNotifierDoesNothing() {
    using var capture = new BackgroundScreenCaptureService(_ => new FixedPngProvider(), 50, NullLogger<BackgroundScreenCaptureService>.Instance);

    var act = () => SessionCaptureCleanup.Attach(new PlainSessionManager(), capture);

    act.Should().NotThrow();
  }

  private sealed class FixedPngProvider : IAdbScreenCaptureProvider {
    private static readonly byte[] Png = CreatePng();

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) => Task.FromResult<byte[]?>(Png);

    private static byte[] CreatePng() {
      using var bmp = new Bitmap(8, 8);
      using var ms = new MemoryStream();
      bmp.Save(ms, ImageFormat.Png);
      return ms.ToArray();
    }
  }

  /// <summary>A session manager that does not implement <see cref="ISessionRemovalNotifier"/>.</summary>
  private sealed class PlainSessionManager : ISessionManager {
    public int ActiveCount => 0;
    public bool CanCreateSession => true;
    public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => new() { Id = "s", GameId = gameIdOrPath };
    public EmulatorSession? GetSession(string id) => null;
    public IReadOnlyCollection<EmulatorSession> ListSessions() => Array.Empty<EmulatorSession>();
    public bool StopSession(string id) => false;
    public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(0);
    public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
      Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));
    public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
  }
}
