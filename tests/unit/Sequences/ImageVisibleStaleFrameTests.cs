using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using GameBot.Domain.Sessions;
using GameBot.Domain.Vision;
using GameBot.UnitTests.Queues;
using Xunit;

#pragma warning disable CA2007

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 197 (FR-006, FR-008): an imageVisible condition on a stale frame.</summary>
public sealed class ImageVisibleStaleFrameTests : IDisposable {
  private static readonly DateTimeOffset T0 = new(2026, 11, 20, 12, 0, 0, TimeSpan.Zero);
  private readonly Bitmap _bitmap = new(1, 1);
  private readonly FakeTimeProvider _clock = new(T0);

  public void Dispose() => _bitmap.Dispose();

  /// <summary>The frame in memory of the test. Null means no frame.</summary>
  private CachedFrame? Current { get; set; }

  private CachedFrame Fresh(string id) => new(new byte[] { 1 }, _bitmap, T0.AddMilliseconds(-300), 1, 1) { FrameId = id };

  private CachedFrame Stale(string id) => new(new byte[] { 1 }, _bitmap, T0.AddSeconds(-10), 1, 1) { FrameId = id };

  /// <summary>
  /// The test image evaluator: it reads <see cref="Current"/> through <see cref="FrameReadScope.Admit"/>, as the
  /// screen sources do. A refused frame is no frame (false). A frame shows the image when <paramref name="showsImage"/>.
  /// </summary>
  private Func<Condition, CancellationToken, Task<bool>> Evaluator(bool showsImage) => (_, _) => {
    var frame = Current;
    var pass = ImageDetectionPassScope.Current;
    if (frame is null || !FrameReadScope.Admit(frame)) {
      if (pass is not null) pass.LastPass = new ImageDetectionPass(false, null, FrameReadScope.Current?.LastRead);
      return Task.FromResult(false);
    }
    if (pass is not null) pass.LastPass = new ImageDetectionPass(true, showsImage ? 0.97 : 0.12, FrameReadScope.Current?.LastRead);
    return Task.FromResult(showsImage);
  };

  /// <summary>A test wait delegate. It runs <paramref name="onWait"/>, then answers as the service delegate does.</summary>
  private FrameReadScope Scope(Action? onWait = null, bool withWait = true) {
    FrameReadScope? scope = null;
    scope = new FrameReadScope(5000, _clock, withWait
      ? _ => {
        onWait?.Invoke();
        var frame = Current;
        if (frame is null) return Task.FromResult(true);
        var info = FrameFreshness.Describe(frame, _clock.GetUtcNow(), scope!.MaxAgeMs);
        if (info.Stale) scope.LastRead = info;
        return Task.FromResult(!info.Stale);
      }
      : null);
    return scope;
  }

  private static ImageVisibleStepCondition Visible(bool negate = false) => new() { ImageId = "boss", Negate = negate };

  private static Task<ConditionEvaluation> EvaluateAsync(SequenceStepCondition condition, Func<Condition, CancellationToken, Task<bool>> evaluator) =>
    SequenceStepConditionEvaluator.EvaluateAsync(condition, evaluator, new Dictionary<string, string>(), CancellationToken.None);

  [Fact]
  public async Task ANegatedConditionOnAStaleFrameIsNeverTrue() {
    // Spec US1 scenario 7: before the fix, the stale frame with no image made the negated condition true.
    Current = Stale("old");
    using var push = FrameReadScope.Push(Scope(withWait: false));

    var act = () => EvaluateAsync(Visible(negate: true), Evaluator(showsImage: false));

    var ex = (await act.Should().ThrowAsync<ConditionEvaluationException>()).Which;
    ex.Kind.Should().Be(ConditionEvaluationFailureKind.StaleFrame);
    ex.Message.Should().Contain("stale_frame").And.Contain("old").And.Contain("10000 ms old");
  }

  [Fact]
  public async Task AStaleFrameThatBecomesFreshInTheWaitGivesTheNormalResult() {
    Current = Stale("old");
    using var push = FrameReadScope.Push(Scope(onWait: () => Current = Fresh("new")));

    (await EvaluateAsync(Visible(), Evaluator(showsImage: true))).Value.Should().BeTrue();
    (await EvaluateAsync(Visible(negate: true), Evaluator(showsImage: true))).Value.Should().BeFalse();
  }

  [Fact]
  public async Task AFrameThatStaysStaleFailsWithStaleFrame() {
    Current = Stale("old");
    using var push = FrameReadScope.Push(Scope());

    var act = () => EvaluateAsync(Visible(), Evaluator(showsImage: true));

    var ex = (await act.Should().ThrowAsync<ConditionEvaluationException>()).Which;
    ex.Kind.Should().Be(ConditionEvaluationFailureKind.StaleFrame);
    ex.Message.Should().Be("stale_frame: the frame old is 10000 ms old (limit 5000 ms).");
  }

  [Fact]
  public async Task AFrameThatBecomesStaleAfterTheWaitFailsWithStaleFrame() {
    Current = Fresh("new");
    // The wait sees a fresh frame. The read sees a stale frame.
    using var push = FrameReadScope.Push(Scope());
    Func<Condition, CancellationToken, Task<bool>> evaluator = (c, ct) => {
      Current = Stale("late");
      return Evaluator(showsImage: false)(c, ct);
    };

    var act = () => EvaluateAsync(Visible(negate: true), evaluator);

    (await act.Should().ThrowAsync<ConditionEvaluationException>()).Which.Kind.Should().Be(ConditionEvaluationFailureKind.StaleFrame);
  }

  [Fact]
  public async Task NoFrameAtAllKeepsTheCurrentResult() {
    Current = null;
    using var push = FrameReadScope.Push(Scope());

    (await EvaluateAsync(Visible(), Evaluator(showsImage: true))).Value.Should().BeFalse();
  }

  [Fact]
  public async Task EachImageCheckWritesALogLineWithTheFrameData() {
    Current = Fresh("f1");
    using var push = FrameReadScope.Push(Scope());
    using var log = CommandStepStateScope.Push(new CommandStepStateScope());

    await EvaluateAsync(Visible(), Evaluator(showsImage: true));

    CommandStepStateScope.DrainLog().Should().Be("imageVisible(boss) capture=f1 ageMs=300 score=0.970");
  }

  [Fact]
  public async Task WithNoFrameDataTheLogLineSaysCaptureNone() {
    using var log = CommandStepStateScope.Push(new CommandStepStateScope());
    Func<Condition, CancellationToken, Task<bool>> evaluator = (_, _) => {
      var pass = ImageDetectionPassScope.Current;
      if (pass is not null) pass.LastPass = new ImageDetectionPass(true, 0.5);
      return Task.FromResult(false);
    };

    await EvaluateAsync(Visible(), evaluator);

    CommandStepStateScope.DrainLog().Should().Be("imageVisible(boss) capture=none score=0.500");
  }

  [Fact]
  public async Task TheRunnerFailsTheStepWithTheStaleFrameMessage() {
    Current = Stale("old");
    var sequence = OcrTextTestKit.Sequence(OcrTextTestKit.Cmd(0, "cmd-a", Visible()));
    using var push = FrameReadScope.Push(Scope());

    var result = await OcrTextTestKit.Runner(sequence).ExecuteAsync(
      "seq",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: Evaluator(showsImage: true),
      ct: CancellationToken.None);

    result.Status.Should().Be("Failed");
    result.Steps.Should().Contain(s => s.Message != null
      && s.Message.StartsWith("Step 'cmd-a' condition evaluation failed: stale_frame: the frame old is 10000 ms old", StringComparison.Ordinal));
  }
}
