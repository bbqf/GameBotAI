#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;
using GameBot.UnitTests.Queues;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// A fake device for the ADB seam of <see cref="SessionManager"/> (feature 106). Each input call can
/// complete, return a non-zero exit code, or hang until its token is cancelled. The fake counts the
/// calls.
/// </summary>
internal sealed class FakeAdbSessionClient : IAdbSessionClient {
  private int _inputCalls;
  private int _screenshotCalls;
  private readonly TaskCompletionSource _firstInputStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);

  /// <summary>The exit code of each input call. Ignored when <see cref="HangInputs"/> is true.</summary>
  public int ExitCode { get; set; }

  /// <summary>When true, each input call hangs until its token is cancelled.</summary>
  public bool HangInputs { get; set; }

  /// <summary>When set, only the input call with this 1-based number hangs.</summary>
  public int? HangOnCall { get; set; }

  /// <summary>When true, each screenshot call hangs until its token is cancelled.</summary>
  public bool HangScreenshots { get; set; }

  public int InputCalls => Volatile.Read(ref _inputCalls);
  public int ScreenshotCalls => Volatile.Read(ref _screenshotCalls);

  /// <summary>Completes when the first input call starts.</summary>
  public Task FirstInputStarted => _firstInputStarted.Task;

  public Task<(int ExitCode, string StdOut, string StdErr)> TapAsync(int x, int y, CancellationToken ct = default) => InputAsync(ct);

  public Task<(int ExitCode, string StdOut, string StdErr)> SwipeAsync(int x1, int y1, int x2, int y2, int? durationMs = null, CancellationToken ct = default) => InputAsync(ct);

  public Task<(int ExitCode, string StdOut, string StdErr)> KeyEventAsync(int keyCode, CancellationToken ct = default) => InputAsync(ct);

  public async Task<byte[]> GetScreenshotPngAsync(CancellationToken ct = default) {
    Interlocked.Increment(ref _screenshotCalls);
    if (HangScreenshots) await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    return new byte[] { 1, 2, 3 };
  }

  private async Task<(int ExitCode, string StdOut, string StdErr)> InputAsync(CancellationToken ct) {
    var n = Interlocked.Increment(ref _inputCalls);
    _firstInputStarted.TrySetResult();
    if (HangInputs || HangOnCall == n) {
      await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    }
    return (ExitCode, string.Empty, string.Empty);
  }
}

/// <summary>Builds a <see cref="SessionManager"/> on the ADB seam with a fake device (feature 106).</summary>
internal static class SeamSessionManager {
  public static SessionManager Create(
    FakeAdbSessionClient device,
    IDeviceLivenessTracker? tracker,
    DeviceLivenessOptions? options = null,
    int adbRetries = 0) =>
    new(
      Options.Create(new SessionOptions()),
      NullLogger<SessionManager>.Instance,
      NullLogger<AdbClient>.Instance,
      new GameBot.Domain.Config.AppConfig { AdbRetries = adbRetries, AdbRetryDelayMs = 0, TapJitterRadiusPx = 0 },
      tracker,
      Options.Create(options ?? new DeviceLivenessOptions()),
      _ => device);

  /// <summary>Creates a session and binds it to a fake device serial.</summary>
  public static string CreateDeviceSession(SessionManager manager, string? serial = "fake-serial") {
    var session = manager.CreateSession("game");
    session.DeviceSerial = serial;
    return session.Id;
  }

  public static FakeTimeProvider NewClock() => new(new DateTimeOffset(2026, 9, 25, 12, 0, 0, TimeSpan.Zero));
}
