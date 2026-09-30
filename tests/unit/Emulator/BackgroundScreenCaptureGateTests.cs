#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator;
using GameBot.Emulator.Session;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 121 (FR-013, FR-014, SC-004, SC-005): the capture loop asks the gate before each capture. A
/// blocked provider gets one call only. A healthy provider keeps its capture rate.
/// </summary>
public sealed class BackgroundScreenCaptureGateTests {
  private sealed class BlockedProvider : IAdbScreenCaptureProvider {
    private int _calls;
    private int _checks;

    public int Calls => Volatile.Read(ref _calls);

    public int Checks => Volatile.Read(ref _checks);

    /// <summary>The answer of the device check. Null means unknown.</summary>
    public bool? Running { get; set; }

    public async Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
      Interlocked.Increment(ref _calls);
      await Task.Delay(Timeout.Infinite, ct);
      return null;
    }

    public Task<bool?> HasRunningScreencapAsync(int timeoutMs, CancellationToken ct) {
      Interlocked.Increment(ref _checks);
      return Task.FromResult(Running);
    }
  }

  private sealed class HealthyProvider : IAdbScreenCaptureProvider {
    private int _calls;
    private static readonly byte[] Png = MakePng();

    public int Calls => Volatile.Read(ref _calls);

    public Task<byte[]?> CaptureScreenshotPngAsync(CancellationToken ct) {
      Interlocked.Increment(ref _calls);
      return Task.FromResult<byte[]?>(Png);
    }

    private static byte[] MakePng() {
      using var bmp = new System.Drawing.Bitmap(2, 2);
      using var ms = new System.IO.MemoryStream();
      bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
      return ms.ToArray();
    }
  }

  private static BackgroundScreenCaptureService Create(IAdbScreenCaptureProvider provider, DeviceCaptureGate gate, int intervalMs = 50) =>
    new(_ => provider, intervalMs, NullLogger<BackgroundScreenCaptureService>.Instance, null,
      new DeviceLivenessOptions { CaptureTimeoutMs = 200, CaptureStallLimitMs = 1000, TransportCheckTimeoutMs = 500 }, gate);

  private static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 5000) {
    var sw = Stopwatch.StartNew();
    while (!condition() && sw.ElapsedMilliseconds < timeoutMs) await Task.Delay(10);
  }

  [Fact]
  public async Task ABlockedProvider_GetsOneCallAndTheDeviceBecomesSuspect() {
    var gate = new DeviceCaptureGate();
    var provider = new BlockedProvider();
    using var service = Create(provider, gate);

    service.StartCapture("s1", "device-1");
    await WaitForAsync(() => gate.IsSuspect("device-1"));
    await Task.Delay(700);

    provider.Calls.Should().Be(1, "a timed-out capture keeps the device Suspect, so no new capture starts");
    gate.IsSuspect("device-1").Should().BeTrue();
  }

  [Fact]
  public async Task ACleanDeviceCheck_ClearsSuspectAndCapturesStartAgain() {
    var gate = new DeviceCaptureGate();
    var provider = new BlockedProvider { Running = false };
    using var service = Create(provider, gate);

    service.StartCapture("s1", "device-1");
    await WaitForAsync(() => provider.Calls >= 2, 8000);

    provider.Checks.Should().BeGreaterThan(0);
    provider.Calls.Should().BeGreaterThanOrEqualTo(2, "the device check found no screencap process");
  }

  [Fact]
  public async Task ACheckThatSaysRunningOrUnknown_KeepsTheDeviceSuspect() {
    var gate = new DeviceCaptureGate();
    var provider = new BlockedProvider { Running = true };
    using var service = Create(provider, gate);

    service.StartCapture("s1", "device-1");
    await WaitForAsync(() => provider.Checks >= 1, 8000);
    provider.Running = null;
    await WaitForAsync(() => provider.Checks >= 2, 8000);

    provider.Calls.Should().Be(1);
    gate.IsSuspect("device-1").Should().BeTrue();
  }

  [Fact]
  public async Task AHealthyProvider_KeepsItsCaptureRate() {
    var gate = new DeviceCaptureGate();
    var provider = new HealthyProvider();
    using var service = Create(provider, gate, intervalMs: 50);

    service.StartCapture("s1", "device-1");
    await Task.Delay(1000);

    provider.Calls.Should().BeGreaterThan(8, "the gate adds no wait for a healthy device");
    gate.IsSuspect("device-1").Should().BeFalse();
  }

  [Fact]
  public async Task StartCaptureForTheSerial_ClearsSuspect() {
    var gate = new DeviceCaptureGate();
    gate.TryBegin("device-1");
    gate.TimedOut("device-1");
    var provider = new HealthyProvider();
    using var service = Create(provider, gate);

    service.StartCapture("s2", "device-1");
    await WaitForAsync(() => provider.Calls >= 2);

    gate.IsSuspect("device-1").Should().BeFalse();
    provider.Calls.Should().BeGreaterThanOrEqualTo(2);
  }
}
