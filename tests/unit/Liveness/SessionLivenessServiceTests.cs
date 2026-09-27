#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.Liveness;
using GameBot.UnitTests.Queues;
using Microsoft.Extensions.Options;
using Xunit;

namespace GameBot.UnitTests.Liveness;

/// <summary>
/// Feature 106 (FR-007 to FR-009, research R-007): the bounded probe of the session health call.
/// </summary>
public sealed class SessionLivenessServiceTests {
  private static readonly DateTimeOffset T0 = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

  private sealed class FakeTransport : ISessionTransportCheck {
    public bool Hang { get; set; }
    public SessionTransportCheckResult Result { get; set; } = new(true, "device", string.Empty, null);
    public int Calls { get; private set; }

    public async Task<SessionTransportCheckResult> CheckAsync(string deviceSerial, CancellationToken ct) {
      Calls++;
      // A hang that ignores the token too: the service must still return in time.
      if (Hang) await Task.Delay(Timeout.Infinite, CancellationToken.None);
      return Result;
    }
  }

  private sealed class FakeCapture : ISessionDirectCapture {
    public bool Hang { get; set; }
    public bool Succeeds { get; set; } = true;
    public int Calls { get; private set; }

    public async Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct) {
      Calls++;
      if (Hang) await Task.Delay(Timeout.Infinite, ct);
      return Succeeds;
    }
  }

  private readonly FakeTimeProvider _clock = new(T0);
  private readonly DeviceLivenessTracker _tracker;
  private readonly FakeTransport _transport = new();
  private readonly FakeCapture _capture = new();
  private readonly SessionLivenessService _service;

  public SessionLivenessServiceTests() {
    _tracker = new DeviceLivenessTracker(_clock);
    _service = new SessionLivenessService(_tracker, _transport, _capture, Options.Create(new DeviceLivenessOptions {
      TransportCheckTimeoutMs = 300,
      CaptureTimeoutMs = 300
    }));
  }

  private static EmulatorSession Session(string? serial = "emu-1") =>
    new() { Id = Guid.NewGuid().ToString("N"), GameId = "g", DeviceSerial = serial };

  [Fact]
  public async Task AHungTransportCheckGivesTransportNotReadyInBoundedTime() {
    _transport.Hang = true;
    var session = Session();

    var sw = Stopwatch.StartNew();
    var result = await _service.ProbeAsync(session, CancellationToken.None);

    sw.ElapsedMilliseconds.Should().BeLessThan(300 + 1000);
    result.Liveness.State.Should().Be(DeviceLivenessStates.NotLive);
    result.Liveness.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
    result.Adb!.Ok.Should().BeFalse();
    result.Adb.Error.Should().Be("adb get-state did not answer in 300 ms");
  }

  [Fact]
  public async Task ATransportFailureGivesTransportNotReady() {
    _transport.Result = new SessionTransportCheckResult(false, "offline", string.Empty, null);

    var result = await _service.ProbeAsync(Session(), CancellationToken.None);

    result.Liveness.Reason.Should().Be(DeviceLivenessReasons.TransportNotReady);
    result.Adb!.Stdout.Should().Be("offline");
  }

  [Fact]
  public async Task NoCaptureDataAndASuccessfulDirectCaptureGiveLive() {
    var result = await _service.ProbeAsync(Session(), CancellationToken.None);

    result.Liveness.State.Should().Be(DeviceLivenessStates.Live);
    result.Liveness.FrameAgeMs.Should().Be(0);
    result.Liveness.NeedsProbe.Should().BeFalse();
    _capture.Calls.Should().Be(1);
  }

  [Theory]
  [InlineData(false)]
  [InlineData(true)]
  public async Task AFailedOrHungDirectCaptureGivesCaptureStalledInBoundedTime(bool hang) {
    _capture.Succeeds = false;
    _capture.Hang = hang;

    var sw = Stopwatch.StartNew();
    var result = await _service.ProbeAsync(Session(), CancellationToken.None);

    sw.ElapsedMilliseconds.Should().BeLessThan(300 + 1000);
    result.Liveness.State.Should().Be(DeviceLivenessStates.NotLive);
    result.Liveness.Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);
  }

  [Fact]
  public async Task TrackerDataGivesNoDirectCapture() {
    var session = Session();
    _tracker.LoopStarted(session.Id);
    _tracker.RecordCapture(session.Id, changed: true);

    var result = await _service.ProbeAsync(session, CancellationToken.None);

    result.Liveness.State.Should().Be(DeviceLivenessStates.Live);
    _capture.Calls.Should().Be(0);
  }

  [Fact]
  public async Task AStubSessionGivesUnknownAndNoCallToTheFakes() {
    var result = await _service.ProbeAsync(Session(serial: null), CancellationToken.None);

    result.Liveness.State.Should().Be(DeviceLivenessStates.Unknown);
    result.Adb.Should().BeNull();
    _transport.Calls.Should().Be(0);
    _capture.Calls.Should().Be(0);
  }

  [Fact]
  public void EvaluateReadsTheTrackerOnly() {
    var session = Session();
    _tracker.LoopStarted(session.Id);
    _clock.Advance(TimeSpan.FromSeconds(61));

    var report = _service.Evaluate(session);

    report.Reason.Should().Be(DeviceLivenessReasons.CaptureStalled);
    _transport.Calls.Should().Be(0);
    _capture.Calls.Should().Be(0);
  }

  [Fact]
  public void OptionsAreNormalized() {
    var service = new SessionLivenessService(_tracker, _transport, _capture, Options.Create(new DeviceLivenessOptions { QueueCheckIntervalMs = 0 }));

    service.Options.QueueCheckIntervalMs.Should().Be(1000);
  }
}
