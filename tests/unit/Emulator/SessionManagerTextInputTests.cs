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
/// Feature 192 and 195: the input action <c>text</c> of <see cref="SessionManager.SendInputsAsync"/>
/// types the text one character at a time, with the character pause between two characters, and
/// with the ADB retries and the liveness records of a key input for each character.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SessionManagerTextInputTests {
  private static InputAction Text(string text) => new("text", new Dictionary<string, object> { ["text"] = text });

  private static (SessionManager Manager, FakeAdbSessionClient Device, string Id) Build(
      int adbRetries = 0, IDeviceLivenessTracker? tracker = null, int textInputCharDelayMs = 0) {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, tracker, adbRetries: adbRetries, textInputCharDelayMs: textInputCharDelayMs);
    SeamSessionManager.RecordPauses(mgr, device);
    return (mgr, device, SeamSessionManager.CreateDeviceSession(mgr));
  }

  [Fact]
  public async Task ANumberGoesToTheDeviceOneCharacterAtATimeWithThePause() {
    var (mgr, device, id) = Build(textInputCharDelayMs: 100);
    var action = Text("250");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(1);
    device.Calls.Should().Equal("text:2", "pause:100", "text:5", "pause:100", "text:0");
    action.Args["typedLength"].Should().Be(3);
  }

  [Fact]
  public async Task ASpaceGoesToTheDeviceAsKeyEvent62() {
    var (mgr, device, id) = Build(textInputCharDelayMs: 100);
    var action = Text("25 0");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(1);
    device.Calls.Should().Equal("text:2", "pause:100", "text:5", "pause:100", "key:62", "pause:100", "text:0");
    action.Args["typedLength"].Should().Be(4);
  }

  [Fact]
  public async Task APauseOfZeroSendsTheCharactersWithNoPause() {
    var (mgr, device, id) = Build(textInputCharDelayMs: 0);

    var accepted = await mgr.SendInputsAsync(id, new[] { Text("250") });

    accepted.Should().Be(1);
    device.Calls.Should().Equal("text:2", "text:5", "text:0");
  }

  [Fact]
  public async Task AOneCharacterTextHasNoPause() {
    var (mgr, device, id) = Build(textInputCharDelayMs: 100);

    await mgr.SendInputsAsync(id, new[] { Text("7") });

    device.Calls.Should().Equal("text:7");
  }

  [Fact]
  public async Task ATextWithPercentSAndAQuoteGoesOneCharacterAtATime() {
    var (mgr, device, id) = Build();

    await mgr.SendInputsAsync(id, new[] { Text("%s'") });

    device.Calls.Should().Equal("text:%", "text:s", "text:'");
  }

  [Theory]
  [InlineData("2", new[] { "text:2" }, 0)]
  [InlineData("5", new[] { "text:2", "text:5" }, 1)]
  [InlineData("0", new[] { "text:2", "text:5", "text:0" }, 2)]
  public async Task AFailedCharacterStopsTheSendAndCountsTheAcceptedCharacters(string failed, string[] calls, int typed) {
    var (mgr, device, id) = Build();
    device.FailTextChunk = failed;
    var action = Text("250");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(0);
    device.Calls.Should().Equal(calls);
    action.Args["typedLength"].Should().Be(typed);
  }

  [Fact]
  public async Task EachCharacterGetsTheAdbRetriesWithNoPauseBetweenRetries() {
    var (mgr, device, id) = Build(adbRetries: 2, textInputCharDelayMs: 100);
    device.FailTextChunk = "5";
    var action = Text("250");

    var accepted = await mgr.SendInputsAsync(id, new[] { action });

    accepted.Should().Be(0);
    device.Calls.Should().Equal("text:2", "pause:100", "text:5", "text:5", "text:5");
    action.Args["typedLength"].Should().Be(1);
  }

  [Theory]
  [InlineData("2 5")]
  [InlineData("250")]
  public async Task TheLivenessTrackerRecordsOneInputForEachAdbCommand(string text) {
    var tracker = new CountingTracker(new DeviceLivenessTracker(SeamSessionManager.NewClock()));
    var (mgr, _, id) = Build(tracker: tracker);

    await mgr.SendInputsAsync(id, new[] { Text(text) });

    tracker.Started.Should().Be(3);
    tracker.Completed.Should().Be(3);
  }

  [Fact]
  public async Task ACancelledPauseStopsTheSend() {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, null, textInputCharDelayMs: 100);
    var id = SeamSessionManager.CreateDeviceSession(mgr);
    using var cts = new CancellationTokenSource();
    mgr.CharacterPause = async (ms, ct) => {
      device.RecordCall("pause:" + ms.ToString(System.Globalization.CultureInfo.InvariantCulture));
      await cts.CancelAsync();
      throw new OperationCanceledException(cts.Token);
    };

    var act = () => mgr.SendInputsAsync(id, new[] { Text("250") }, cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
    device.Calls.Should().Equal("text:2", "pause:100");
  }

  [Fact]
  public async Task StubModeCountsTheActionAndDoesNotSetTheTypedLength() {
    var device = new FakeAdbSessionClient();
    var mgr = SeamSessionManager.Create(device, null, textInputCharDelayMs: 100);
    SeamSessionManager.RecordPauses(mgr, device);
    var session = mgr.CreateSession("game");
    var action = Text("250");

    var accepted = await mgr.SendInputsAsync(session.Id, new[] { action });

    accepted.Should().Be(1);
    action.Args.Should().NotContainKey("typedLength");
    device.InputCalls.Should().Be(0);
    device.Calls.Should().NotContain(c => c.StartsWith("pause:", StringComparison.Ordinal));
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
