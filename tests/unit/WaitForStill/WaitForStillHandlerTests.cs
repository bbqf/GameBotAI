using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Config;
using GameBot.Domain.WaitForStill;
using GameBot.Service.Services.SequenceExecution;
using GameBot.Service.Services.WaitForStill;
using Xunit;

#pragma warning disable CA1416, CA2007, CA2000

namespace GameBot.UnitTests.WaitForStill;

/// <summary>
/// Feature 174: the capture loop of the WaitForStill step. A manual clock moves forward only when the handler waits,
/// so each time in these tests is exact. The capture interval is 500 ms.
/// </summary>
public sealed class WaitForStillHandlerTests {
  // A clock that moves by the wait time when a wait starts. The wait ends at once.
  private sealed class StepClock : TimeProvider {
    private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) {
      if (dueTime > TimeSpan.Zero) {
        _now += dueTime;
      }

      ThreadPool.QueueUserWorkItem(_ => callback(state));
      return new NoTimer();
    }

    private sealed class NoTimer : ITimer {
      public bool Change(TimeSpan dueTime, TimeSpan period) => true;
      public void Dispose() { }
      public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
  }

  // Gives the scripted frames in order and then repeats the last one. A null entry is a failed capture.
  private sealed class ScriptedFrames : ISessionFrameSource {
    private readonly List<Func<Bitmap?>> _script;
    private int _index;

    public ScriptedFrames(params Func<Bitmap?>[] script) => _script = new List<Func<Bitmap?>>(script);

    public int Calls { get; private set; }

    public Action<int>? OnCall { get; set; }

    public Bitmap? Capture(string sessionId) {
      Calls++;
      OnCall?.Invoke(Calls);
      var item = _script[Math.Min(_index, _script.Count - 1)];
      _index++;
      return item();
    }
  }

  private static Func<Bitmap?> Frame(Color color) => () => Solid(color);

  private static Func<Bitmap?> Failed => () => null;

  private static Func<Bitmap?> Throws => () => throw new InvalidOperationException("capture failed");

  private static Bitmap Solid(Color color, int width = 40, int height = 30) {
    var bitmap = new Bitmap(width, height);
    using var g = Graphics.FromImage(bitmap);
    g.Clear(color);
    return bitmap;
  }

  private static readonly Color A = Color.FromArgb(10, 10, 10);
  private static readonly Color B = Color.FromArgb(200, 200, 200);
  private static readonly Color C = Color.FromArgb(100, 20, 150);

  private static SequenceActionPayload Action(string json) {
    var action = new SequenceActionPayload { Type = ActionTypes.WaitForStill };
    foreach (var property in System.Text.Json.JsonDocument.Parse(json).RootElement.EnumerateObject()) {
      action.Parameters[property.Name] = property.Value.Clone();
    }

    return action;
  }

  private static (WaitForStillHandler Handler, StepClock Clock) NewHandler(ISessionFrameSource? frames) {
    var clock = new StepClock();
    return (new WaitForStillHandler(new AppConfig { CaptureIntervalMs = 500 }, frames, clock), clock);
  }

  [Fact]
  public async Task AStillScreenEndsWithStillAfterOneInterval() {
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A)));

    var result = await handler.ExecuteAsync(Action("{}"), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
    result.Message.Should().StartWith("still after 500 ms; last difference 0.0000");
  }

  [Fact]
  public async Task AScreenThatMovesAndThenStopsEndsWithStill() {
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A), Frame(B), Frame(C), Frame(C)));

    var result = await handler.ExecuteAsync(Action("""{"stableMs":900}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
    result.Message.Should().StartWith("still after 2000 ms");
  }

  [Fact]
  public async Task AScreenThatNeverStopsEndsWithTimeoutElapsedAtTheTimeLimit() {
    // The script repeats its last frame, so the list is longer than the number of passes.
    var frames = new ScriptedFrames(
      Frame(A), Frame(B), Frame(A), Frame(B), Frame(A), Frame(B), Frame(A), Frame(B), Frame(A), Frame(B));
    var (handler, clock) = NewHandler(frames);
    var start = clock.GetUtcNow();

    var result = await handler.ExecuteAsync(Action("""{"timeoutMs":2000}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.TimeoutElapsed);
    result.Message.Should().Be("not still after 2000 ms; last difference 0.7451");
    (clock.GetUtcNow() - start).Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(2000));
  }

  [Fact]
  public async Task ANullSessionWaitsForTheLimitAndGivesTimeoutElapsed() {
    var frames = new ScriptedFrames(Frame(A));
    var (handler, clock) = NewHandler(frames);
    var start = clock.GetUtcNow();

    var result = await handler.ExecuteAsync(Action("""{"timeoutMs":800}"""), null, CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.TimeoutElapsed);
    result.Message.Should().Be("the screen could not be read");
    frames.Calls.Should().Be(0);
    (clock.GetUtcNow() - start).Should().Be(TimeSpan.FromMilliseconds(800));
  }

  [Fact]
  public async Task AHostWithoutAFrameSourceGivesTimeoutElapsed() {
    var (handler, _) = NewHandler(null);

    var result = await handler.ExecuteAsync(Action("""{"timeoutMs":800}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.TimeoutElapsed);
    result.Message.Should().Be("the screen could not be read");
  }

  [Fact]
  public async Task ANullFrameIsSkippedAndTheNextGoodFrameIsABaseline() {
    var (handler, _) = NewHandler(new ScriptedFrames(Failed, Frame(A), Frame(A)));

    var result = await handler.ExecuteAsync(Action("{}"), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
    result.Message.Should().StartWith("still after 1000 ms");
  }

  [Fact]
  public async Task ACaptureThatThrowsIsSkippedNotFailed() {
    var (handler, _) = NewHandler(new ScriptedFrames(Throws, Frame(A), Frame(A)));

    var result = await handler.ExecuteAsync(Action("{}"), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
    result.Message.Should().StartWith("still after 1000 ms");
  }

  [Fact]
  public async Task AFailedCaptureRestartsTheStableTime() {
    // Without the restart the step would end with still at 1500 ms (the null frame at 1000 ms is skipped).
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A), Frame(A), Failed, Frame(A), Frame(A), Frame(A)));

    var result = await handler.ExecuteAsync(Action("""{"stableMs":800}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
    result.Message.Should().StartWith("still after 2500 ms");
  }

  [Fact]
  public async Task TimeSpentInFailedCapturesNeverCountsAsStableTime() {
    // Frames: A, failed, A. The two good frames are 1000 ms apart but a failed capture is between them.
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A), Failed, Frame(A)));

    var result = await handler.ExecuteAsync(Action("""{"stableMs":1000,"timeoutMs":1500}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.TimeoutElapsed);
  }

  [Fact]
  public async Task AScreenThatCannotBeReadUntilTheLimitGivesTimeoutElapsed() {
    var frames = new ScriptedFrames(Failed);
    var (handler, clock) = NewHandler(frames);
    var start = clock.GetUtcNow();

    var result = await handler.ExecuteAsync(Action("""{"timeoutMs":2000}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.TimeoutElapsed);
    result.Message.Should().Be("the screen could not be read");
    frames.Calls.Should().BeGreaterThan(1);
    (clock.GetUtcNow() - start).Should().BeLessThanOrEqualTo(TimeSpan.FromMilliseconds(2000));
  }

  [Fact]
  public async Task AStopSignalDuringTheWaitPassesUpAsCancellation() {
    using var cts = new CancellationTokenSource();
    var frames = new ScriptedFrames(Frame(A), Frame(B)) { OnCall = call => { if (call == 2) cts.Cancel(); } };
    var (handler, _) = NewHandler(frames);

    var act = () => handler.ExecuteAsync(Action("""{"timeoutMs":5000}"""), "s1", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
  }

  [Fact]
  public async Task ACancelInsideTheCaptureCallIsNotTreatedAsAFailedCapture() {
    using var cts = new CancellationTokenSource();
    Func<Bitmap?> cancelling = () => throw new OperationCanceledException(cts.Token);
    var (handler, _) = NewHandler(new ScriptedFrames(cancelling));

    var act = () => handler.ExecuteAsync(Action("{}"), "s1", cts.Token);

    await act.Should().ThrowAsync<OperationCanceledException>();
  }

  [Fact]
  public async Task AnInvalidPayloadGivesFailed() {
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A)));

    var result = await handler.ExecuteAsync(Action("""{"stableMs":0}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be("failed");
    result.Message.Should().Contain("stableMs");
  }

  [Theory]
  [InlineData("""{}""")]
  [InlineData("""{"timeoutMs":1000}""")]
  [InlineData("""{"region":{"x":0,"y":0,"width":10,"height":10}}""")]
  public async Task TheOutcomeIsAlwaysStillOrTimeoutElapsedForGoodRuns(string json) {
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A), Frame(B), Failed, Frame(C)));

    var result = await handler.ExecuteAsync(Action(json), "s1", CancellationToken.None);

    WaitForStillOutcomes.IsWaitForStillOutcome(result.Outcome).Should().BeTrue();
  }

  // ---- region (User Story 2) ----

  private static Func<Bitmap?> WithPixel(Color background, int x, int y, Color pixel) => () => {
    var bitmap = Solid(background);
    bitmap.SetPixel(x, y, pixel);
    return bitmap;
  };

  [Fact]
  public async Task AChangeOutsideTheRegionHasNoEffect() {
    var (handler, _) = NewHandler(new ScriptedFrames(
      WithPixel(A, 35, 25, Color.White), WithPixel(A, 35, 25, Color.Black), WithPixel(A, 35, 25, Color.White)));

    var result = await handler.ExecuteAsync(
      Action("""{"region":{"x":0,"y":0,"width":20,"height":20},"timeoutMs":3000}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
  }

  [Fact]
  public async Task AChangeInsideTheRegionResetsTheWait() {
    var (handler, _) = NewHandler(new ScriptedFrames(
      WithPixel(A, 5, 5, Color.White), WithPixel(A, 5, 5, Color.Black), WithPixel(A, 5, 5, Color.White),
      WithPixel(A, 5, 5, Color.Black), WithPixel(A, 5, 5, Color.White), WithPixel(A, 5, 5, Color.Black),
      WithPixel(A, 5, 5, Color.White)));

    var result = await handler.ExecuteAsync(
      Action("""{"region":{"x":0,"y":0,"width":4,"height":4},"timeoutMs":3000}"""), "s1", CancellationToken.None);
    var inside = await NewHandler(new ScriptedFrames(
      WithPixel(A, 2, 2, Color.White), WithPixel(A, 2, 2, Color.Black), WithPixel(A, 2, 2, Color.White),
      WithPixel(A, 2, 2, Color.Black), WithPixel(A, 2, 2, Color.White), WithPixel(A, 2, 2, Color.Black),
      WithPixel(A, 2, 2, Color.White))).Handler.ExecuteAsync(
      Action("""{"region":{"x":0,"y":0,"width":4,"height":4},"timeoutMs":3000}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still, "the changing pixel is outside the 4x4 region");
    inside.Outcome.Should().Be(WaitForStillOutcomes.TimeoutElapsed, "the changing pixel is inside the region");
  }

  [Fact]
  public async Task ARegionThatIsPartlyOutsideTheScreenIsCut() {
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A)));

    var result = await handler.ExecuteAsync(
      Action("""{"region":{"x":30,"y":20,"width":500,"height":500}}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be(WaitForStillOutcomes.Still);
  }

  [Fact]
  public async Task ARegionFullyOutsideTheScreenGivesFailedAndNoNewOutcomeName() {
    var (handler, _) = NewHandler(new ScriptedFrames(Frame(A)));

    var result = await handler.ExecuteAsync(
      Action("""{"region":{"x":100,"y":100,"width":10,"height":10}}"""), "s1", CancellationToken.None);

    result.Outcome.Should().Be("failed");
    result.Message.Should().Be("region is outside the screen (40x30)");
    WaitForStillOutcomes.IsWaitForStillOutcome(result.Outcome).Should().BeFalse();
  }
}
