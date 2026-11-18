#pragma warning disable CA2007 // test code: no ConfigureAwait
using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using Xunit;

namespace GameBot.UnitTests.Emulator;

/// <summary>
/// Feature 192: the input action <c>text</c> of <see cref="SessionManager.SendInputsAsync"/> types the
/// text as chunks and space key events, with the ADB retries and the liveness records of a key input.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerTextInputTests {
  private static InputAction Text(string text) => new("text", new Dictionary<string, object> { ["text"] = text });

  private static (SessionManager Manager, FakeAdbSessionClient Device, string Id) Build(
      int adbRetries = 0, IDeviceLivenessTracker? tracker = null) {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, tracker, adbRetries: adbRetries);
    return (mgr, device, SeamSessionManager.CreateDeviceSession(mgr));
  }

  [Fact]
  public async Task ANumberGoesToTheDeviceAsOneTextCommand() {
    var (mgr, device, id) = Build();
    var action = Text("250");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(1);
    device.TextChunks.Should().Equal("250");
    device.KeyCodes.Should().BeEmpty();
    action.Args["typedLength"].Should().Be(3);
  }

  [Fact]
  public async Task ASpaceGoesToTheDeviceAsKeyEvent62() {
    var (mgr, device, id) = Build();
    var action = Text("25 0");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(1);
    device.Calls.Should().Equal("text:25", "key:62", "text:0");
    action.Args["typedLength"].Should().Be(4);
  }

  [Fact]
  public async Task AFailedChunkStopsTheSendAndKeepsTheTypedLength() {
    var (mgr, device, id) = Build();
    device.FailTextChunk = "Fighters";
    var action = Text("250 Fighters now");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(0);
    device.Calls.Should().Equal("text:250", "key:62", "text:Fighters");
    action.Args["typedLength"].Should().Be(4);
  }

  [Fact]
  public async Task AChunkGetsTheConfiguredAdbRetries() {
    var (mgr, device, id) = Build(adbRetries: 2);
    device.FailTextChunk = "250";

    var accepted = await mgr.SendInputsAsync(id, new[] { Text("250") });

    accepted.Should().Be(0);
    device.TextChunks.Should().Equal("250", "250", "250");
  }

  [Fact]
  public async Task TheLivenessTrackerRecordsOneInputForEachAdbCommand() {
    var tracker = new CountingTracker(new DeviceLivenessTracker(SeamSessionManager.NewClock()));
    var (mgr, _, id) = Build(tracker: tracker);

    await mgr.SendInputsAsync(id, new[] { Text("2 5") });

    tracker.Started.Should().Be(3);
    tracker.Completed.Should().Be(3);
  }

  [Fact]
  public async Task StubModeCountsTheActionAndDoesNotSetTheTypedLength() {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, null);
    var session = mgr.CreateSession("game");
    var action = Text("250");

    var accepted = await mgr.SendInputsAsync(session.Id, new[] { action });

    accepted.Should().Be(1);
    action.Args.Should().NotContainKey("typedLength");
    device.InputCalls.Should().Be(0);
  }

  [Fact]
  public async Task AKeyActionWithTheKey250StillSendsKeyCode250() {
    var (mgr, device, id) = Build();

    var accepted = await mgr.SendInputsAsync(id, new[] { new InputAction("key", new Dictionary<string, object> { ["key"] = "250" }) });

    accepted.Should().Be(1);
    device.KeyCodes.Should().Equal(250);
    device.TextChunks.Should().BeEmpty();
  }

  private sealed class CountingTracker : IDeviceLivenessTracker {
    private readonly IDeviceLivenessTracker _inner;
    private int _started;
    private int _completed;

    public CountingTracker(IDeviceLivenessTracker inner) => _inner = inner;

    public int Started => Volatile.Read(ref _started);
    public int Completed => Volatile.Read(ref _completed);
    public DateTimeOffset Now => _inner.Now;
    public void LoopStarted(string sessionId) => _inner.LoopStarted(sessionId);
    public void LoopStopped(string sessionId) => _inner.LoopStopped(sessionId);
    public void RecordCapture(string sessionId, bool changed) => _inner.RecordCapture(sessionId, changed);

    public void RecordInputStarted(string sessionId) {
      Interlocked.Increment(ref _started);
      _inner.RecordInputStarted(sessionId);
    }

    public void RecordInputCompleted(string sessionId, InputOutcome outcome) {
      Interlocked.Increment(ref _completed);
      _inner.RecordInputCompleted(sessionId, outcome);
    }

    public void Remove(string sessionId) => _inner.Remove(sessionId);
    public DeviceLivenessSample Sample(string sessionId, bool hasDevice, bool? transportReady = null) =>
      _inner.Sample(sessionId, hasDevice, transportReady);
    public bool HasCaptureData(string sessionId) => _inner.HasCaptureData(sessionId);
  }
}
