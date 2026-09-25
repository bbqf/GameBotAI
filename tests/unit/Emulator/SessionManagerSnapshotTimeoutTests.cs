#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 106 (FR-011, research R-008): a cancel of the snapshot goes to the caller. It never becomes
/// the 1x1 stub PNG.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerSnapshotTimeoutTests {
  [Fact]
  public async Task ACancelledHungScreenshotThrowsAndDoesNotReturnTheStubPng() {
    var device = new FakeAdbSessionClient { HangScreenshots = true };
    var mgr = SeamSessionManager.Create(device, new DeviceLivenessTracker(SeamSessionManager.NewClock()), adbRetries: 2);
    var id = SeamSessionManager.CreateDeviceSession(mgr);
    using var cts = new CancellationTokenSource(200);

    var act = async () => await mgr.GetSnapshotAsync(id, cts.Token).WaitAsync(TimeSpan.FromSeconds(5));

    await act.Should().ThrowAsync<OperationCanceledException>();
    device.ScreenshotCalls.Should().Be(1, "a cancel is not retried");
  }

  [Fact]
  public async Task ACompletedScreenshotReturnsTheDeviceBytes() {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, null);
    var id = SeamSessionManager.CreateDeviceSession(mgr);

    var png = await mgr.GetSnapshotAsync(id);

    png.Should().Equal(1, 2, 3);
  }
}
