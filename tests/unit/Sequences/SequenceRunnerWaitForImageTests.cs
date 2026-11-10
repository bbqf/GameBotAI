using System.Diagnostics;
using FluentAssertions;
using GameBot.Domain.Config;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GameBot.UnitTests.Sequences;

public sealed class SequenceRunnerWaitForImageTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) {
      _sequence = sequence;
    }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  [Fact]
  public async Task WaitForImageDetectsBeforeTimeoutAndCompletesNormally() {
    var executed = new List<string>();
    var invocationCount = 0;
    var runner = CreateRunner(new WaitForImageConfig {
      TimeoutMs = 120,
      DetectionTarget = new DetectionTarget("map-image", 0.85)
    });

    var result = await runner.ExecuteAsync(
      "wait-sequence",
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(++invocationCount >= 3),
      ct: CancellationToken.None).ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-after-wait");
    result.Steps.Should().ContainSingle(step => step.ActionOutcome == "image_detected");
  }

  [Fact]
  public async Task WaitForImageWithoutConfiguredImageWaitsUntilTimeoutAndContinues() {
    var executed = new List<string>();
    var runner = CreateRunner(new WaitForImageConfig {
      TimeoutMs = 40,
      DetectionTarget = null
    });

    var stopwatch = Stopwatch.StartNew();
    var result = await runner.ExecuteAsync(
      "wait-sequence",
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None).ConfigureAwait(false);
    stopwatch.Stop();

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-after-wait");
    // No image to look for means this is a plain delay, reported as such rather than as a search
    // that timed out.
    result.Steps.Should().ContainSingle(step => step.ActionOutcome == "delay_elapsed");
    stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(25);
  }

  [Fact]
  public async Task WaitForImageWhenImageIsUnavailableStillWaitsAndContinues() {
    var executed = new List<string>();
    var runner = CreateRunner(new WaitForImageConfig {
      TimeoutMs = 40,
      DetectionTarget = new DetectionTarget("missing-image", 0.90)
    });

    var stopwatch = Stopwatch.StartNew();
    var result = await runner.ExecuteAsync(
      "wait-sequence",
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => throw new InvalidOperationException("image unavailable"),
      ct: CancellationToken.None).ConfigureAwait(false);
    stopwatch.Stop();

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-after-wait");
    result.Steps.Should().ContainSingle(step => step.ActionOutcome == "image_unavailable");
    stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(25);
  }

  [Fact]
  public async Task WaitForImageStartsNoEvaluationAtOrAfterTheDeadline() {
    // Feature 166 (FR-010): each evaluation takes 150 ms and gives false. With timeoutMs 300 and a poll
    // interval of 1000 ms, the step does two evaluations, and the last wait ends 50 ms before the deadline.
    var runner = new SequenceRunner(
      new StubRepo(BuildSequence(new WaitForImageConfig { TimeoutMs = 300, DetectionTarget = new DetectionTarget("map-image", 0.85) })),
      NullLogger<SequenceRunner>.Instance,
      new AppConfig { CaptureIntervalMs = 1000 });
    var clock = new Stopwatch();
    var offsets = new List<long>();

    var result = await runner.ExecuteAsync(
      "wait-sequence",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => {
        // The first evaluation is the start of the step.
        if (!clock.IsRunning) clock.Start();
        offsets.Add(clock.ElapsedMilliseconds);
        // A synchronous pass, as the image matcher of the service is.
#pragma warning disable CA1849
        Thread.Sleep(150);
#pragma warning restore CA1849
        return Task.FromResult(false);
      },
      ct: CancellationToken.None).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "timeout_elapsed");
    offsets.Should().HaveCount(2);
    // timeoutMs - 50 ms + 30 ms tolerance for the timer resolution.
    offsets.Should().OnlyContain(offset => offset < 280);
  }

  [Fact]
  public async Task WaitForImageTimeoutWritesOneDebugLineWithThePassOfTheEvaluator() {
    // Feature 166 (FR-012): the evaluator writes the pass into the ambient scope of the wait.
    var logger = new CapturingLogger();
    var runner = new SequenceRunner(
      new StubRepo(BuildSequence(new WaitForImageConfig { TimeoutMs = 60, DetectionTarget = new DetectionTarget("map-image", 0.85) })),
      logger,
      new AppConfig { CaptureIntervalMs = 20 });

    await runner.ExecuteAsync(
      "wait-sequence",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => {
        var scope = GameBot.Domain.Vision.ImageDetectionPassScope.Current;
        if (scope is not null) scope.LastPass = new GameBot.Domain.Vision.ImageDetectionPass(true, 0.423);
        return Task.FromResult(false);
      },
      ct: CancellationToken.None).ConfigureAwait(false);

    var line = SingleTimeoutLine(logger);
    line.Should().Contain("wait-sequence");
    line.Should().Contain("wait-step");
    line.Should().Contain("map-image");
    line.Should().Contain("0.85");
    line.Should().MatchRegex(@"\d+ passes");
    line.Should().Contain("frame available true");
    line.Should().Contain("best score 0.423");
  }

  [Fact]
  public async Task WaitForImageTimeoutWritesUnknownFrameStateWhenNoEvaluatorWrites() {
    var logger = new CapturingLogger();
    var runner = new SequenceRunner(
      new StubRepo(BuildSequence(new WaitForImageConfig { TimeoutMs = 60, DetectionTarget = new DetectionTarget("map-image", 0.85) })),
      logger,
      new AppConfig { CaptureIntervalMs = 20 });

    await runner.ExecuteAsync(
      "wait-sequence",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None).ConfigureAwait(false);

    var line = SingleTimeoutLine(logger);
    line.Should().Contain("frame available unknown");
    line.Should().Contain("best score none");
  }

  // ---- Feature 175: WaitForImage until absent ----

  private static WaitForImageConfig AbsentConfig(int timeoutMs, int? absentCaptures = null, DetectionTarget? target = null) =>
    new() {
      TimeoutMs = timeoutMs,
      DetectionTarget = target ?? new DetectionTarget("toast", 0.85),
      Until = "absent",
      AbsentCaptures = absentCaptures
    };

  /// <summary>
  /// A fake evaluator that plays a script of passes, as the image evaluator writes them. After the script
  /// ends, the last pass repeats.
  /// </summary>
  private static Func<GameBot.Domain.Commands.Blocks.Condition, CancellationToken, Task<bool>> Script(
      List<GameBot.Domain.Commands.Blocks.Condition>? seenConditions, params PassKindScript[] passes) {
    var index = 0;
    return (condition, _) => {
      seenConditions?.Add(condition);
      var pass = passes[Math.Min(index++, passes.Length - 1)];
      var scope = GameBot.Domain.Vision.ImageDetectionPassScope.Current;
      switch (pass) {
        case PassKindScript.Seen:
          if (scope is not null) scope.LastPass = new GameBot.Domain.Vision.ImageDetectionPass(true, 0.95);
          return Task.FromResult(true);
        case PassKindScript.Absent:
          if (scope is not null) scope.LastPass = new GameBot.Domain.Vision.ImageDetectionPass(true, 0.1);
          return Task.FromResult(false);
        default:
          if (scope is not null) scope.LastPass = new GameBot.Domain.Vision.ImageDetectionPass(false, null);
          return Task.FromResult(false);
      }
    };
  }

  private enum PassKindScript { Seen, Absent, NoFrame }

  private static async Task<GameBot.Domain.Services.SequenceExecutionResult> RunAsync(
      SequenceRunner runner,
      Func<GameBot.Domain.Commands.Blocks.Condition, CancellationToken, Task<bool>>? evaluator,
      CancellationToken ct = default) =>
    await runner.ExecuteAsync("wait-sequence", (_, _) => Task.CompletedTask, conditionEvaluator: evaluator, ct: ct).ConfigureAwait(false);

  [Fact]
  public async Task AbsentModeEndsWhenImageLeaves() {
    var runner = CreateRunner(AbsentConfig(2000));
    var result = await RunAsync(runner, Script(null, PassKindScript.Seen, PassKindScript.Seen, PassKindScript.Absent)).ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().Contain(step => step.ActionOutcome == "image_absent");
  }

  [Fact]
  public async Task AbsentModeEndsAtOnceWhenImageIsAlreadyAbsent() {
    var runner = CreateRunner(AbsentConfig(2000));
    var stopwatch = Stopwatch.StartNew();
    var result = await RunAsync(runner, Script(null, PassKindScript.Absent)).ConfigureAwait(false);
    stopwatch.Stop();

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_absent");
    stopwatch.ElapsedMilliseconds.Should().BeLessThan(1000);
  }

  [Fact]
  public async Task AbsentModeEvaluatesInPresentMode() {
    var seen = new List<GameBot.Domain.Commands.Blocks.Condition>();
    var runner = CreateRunner(AbsentConfig(2000));
    await RunAsync(runner, Script(seen, PassKindScript.Absent)).ConfigureAwait(false);

    seen.Should().NotBeEmpty();
    seen.Should().OnlyContain(c => c.Mode == "Present");
  }

  [Fact]
  public async Task AbsentModeTimesOutWhenImageStays() {
    var runner = CreateRunner(AbsentConfig(80));
    var result = await RunAsync(runner, Script(null, PassKindScript.Seen)).ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    result.Steps.Should().Contain(step => step.ActionOutcome == "timeout_elapsed");
  }

  [Fact]
  public async Task AbsentModeWithoutEvaluatorGivesImageUnavailable() {
    var runner = CreateRunner(AbsentConfig(40));
    var result = await RunAsync(runner, null).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_unavailable");
  }

  [Fact]
  public async Task AbsentModeWhenEvaluatorThrowsGivesImageUnavailable() {
    var runner = CreateRunner(AbsentConfig(40));
    var result = await RunAsync(runner, (_, _) => throw new InvalidOperationException("boom")).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_unavailable");
  }

  [Fact]
  public async Task AbsentModeWithoutImageIdIsAPlainDelay() {
    var runner = CreateRunner(new WaitForImageConfig { TimeoutMs = 30, Until = "absent" });
    var result = await RunAsync(runner, Script(null, PassKindScript.Absent)).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "delay_elapsed");
  }

  [Fact]
  public async Task AbsentModeCancellationRecordsNoOutcome() {
    using var cts = new CancellationTokenSource();
    var runner = CreateRunner(AbsentConfig(5000));
    var evaluator = Script(null, PassKindScript.Seen);
    var calls = 0;
    var act = async () => await RunAsync(runner, async (c, t) => {
      if (++calls == 2) await cts.CancelAsync().ConfigureAwait(false);
      return await evaluator(c, t).ConfigureAwait(false);
    }, cts.Token).ConfigureAwait(false);

    await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);
  }

  [Fact]
  public async Task AbsentModeRecordsSuccessInStepOutcomes() {
    var runner = new SequenceRunner(
      new StubRepo(BuildSequenceWithOutcomeCondition(AbsentConfig(2000))),
      NullLogger<SequenceRunner>.Instance,
      new AppConfig { CaptureIntervalMs = 10 });
    var executed = new List<string>();
    var result = await runner.ExecuteAsync(
      "wait-sequence",
      (commandId, _) => { executed.Add(commandId); return Task.CompletedTask; },
      conditionEvaluator: Script(null, PassKindScript.Absent),
      ct: CancellationToken.None).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_absent");
    executed.Should().ContainSingle().Which.Should().Be("cmd-after-wait");
  }

  [Fact]
  public async Task FlickerWithTwoCapturesDoesNotEndTheWait() {
    // seen, absent, seen, absent, absent: ends only at the last two passes.
    var runner = CreateRunner(AbsentConfig(2000, 2));
    var calls = 0;
    var evaluator = Script(null, PassKindScript.Seen, PassKindScript.Absent, PassKindScript.Seen, PassKindScript.Absent, PassKindScript.Absent);
    var result = await RunAsync(runner, (c, t) => { calls++; return evaluator(c, t); }).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_absent");
    calls.Should().Be(5);
  }

  [Fact]
  public async Task DefaultNeedsTwoAbsentCaptures() {
    var runner = CreateRunner(AbsentConfig(2000));
    var calls = 0;
    var evaluator = Script(null, PassKindScript.Absent);
    await RunAsync(runner, (c, t) => { calls++; return evaluator(c, t); }).ConfigureAwait(false);

    calls.Should().Be(2);
  }

  [Fact]
  public async Task OneAbsentCaptureEndsAtTheFirstAbsentPass() {
    var runner = CreateRunner(AbsentConfig(2000, 1));
    var calls = 0;
    var evaluator = Script(null, PassKindScript.Seen, PassKindScript.Absent);
    await RunAsync(runner, (c, t) => { calls++; return evaluator(c, t); }).ConfigureAwait(false);

    calls.Should().Be(2);
  }

  [Fact]
  public async Task FailedCaptureResetsTheCount() {
    // absent, no frame, absent: the count restarts, so the wait times out with absentCaptures 2.
    var runner = CreateRunner(AbsentConfig(150, 2));
    var evaluator = Script(null, PassKindScript.Absent, PassKindScript.NoFrame, PassKindScript.Absent, PassKindScript.NoFrame, PassKindScript.Absent, PassKindScript.NoFrame);
    var result = await RunAsync(runner, evaluator).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "timeout_elapsed");
  }

  [Fact]
  public async Task PassWithNoWrittenPassNeverCountsAsAbsent() {
    var runner = CreateRunner(AbsentConfig(80, 1));
    var result = await RunAsync(runner, (_, _) => Task.FromResult(false)).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "timeout_elapsed");
  }

  [Fact]
  public async Task ShortTimeoutGivesTimeoutElapsedWhenImageNeverLeaves() {
    var runner = CreateRunner(AbsentConfig(30, 2));
    var result = await RunAsync(runner, Script(null, PassKindScript.Seen)).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "timeout_elapsed");
  }

  [Fact]
  public async Task AbsentModeEndsBeforeTheFixedWaitWhenImageLeaves() {
    // SC-004 with scaled time: the capture interval is 50 ms and the timeout is 2000 ms. The image leaves
    // after 100 ms. The step must end long before the timeout. The runner has no injectable clock, so a
    // scaled real time stands in for the fake clock.
    var runner = new SequenceRunner(
      new StubRepo(BuildSequence(AbsentConfig(2000, 2))),
      NullLogger<SequenceRunner>.Instance,
      new AppConfig { CaptureIntervalMs = 50 });
    var clock = Stopwatch.StartNew();
    var evaluator = new Func<GameBot.Domain.Commands.Blocks.Condition, CancellationToken, Task<bool>>((_, _) => {
      var gone = clock.ElapsedMilliseconds >= 100;
      var scope = GameBot.Domain.Vision.ImageDetectionPassScope.Current;
      if (scope is not null) scope.LastPass = new GameBot.Domain.Vision.ImageDetectionPass(true, gone ? 0.1 : 0.95);
      return Task.FromResult(!gone);
    });

    var result = await RunAsync(runner, evaluator).ConfigureAwait(false);
    clock.Stop();

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_absent");
    // Gone at 100 ms, plus two captures of 50 ms, plus timer tolerance. Far below the 2000 ms timeout.
    clock.ElapsedMilliseconds.Should().BeLessThan(1000);
  }

  [Fact]
  public async Task RegionOutsideTheImageCountsAsAbsent() {
    var region = new PixelRegion(0, 0, 10, 10);
    var target = new DetectionTarget("toast", 0.85, region: region);
    var seen = new List<GameBot.Domain.Commands.Blocks.Condition>();
    var runner = CreateRunner(AbsentConfig(2000, 1, target));
    var result = await RunAsync(runner, Script(seen, PassKindScript.Absent)).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_absent");
    seen.Should().OnlyContain(c => c.PixelRegion != null && c.PixelRegion.Width == 10);
  }

  [Fact]
  public async Task StepWithoutUntilKeepsPresentBehavior() {
    var invocationCount = 0;
    var runner = CreateRunner(new WaitForImageConfig { TimeoutMs = 120, DetectionTarget = new DetectionTarget("map-image", 0.85) });
    var result = await RunAsync(runner, (_, _) => Task.FromResult(++invocationCount >= 2)).ConfigureAwait(false);

    result.Steps.Should().Contain(step => step.ActionOutcome == "image_detected");
  }

  private static CommandSequence BuildSequenceWithOutcomeCondition(WaitForImageConfig waitConfig) => BuildSequence(waitConfig);

  private static string SingleTimeoutLine(CapturingLogger logger) {
    var lines = logger.Entries
      .Where(e => e.Level == Microsoft.Extensions.Logging.LogLevel.Debug && e.Message.Contains("timed out", StringComparison.Ordinal))
      .ToList();
    lines.Should().ContainSingle();
    return lines[0].Message;
  }

  private sealed class CapturingLogger : Microsoft.Extensions.Logging.ILogger<SequenceRunner> {
    private readonly List<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> _entries = new();
    public IReadOnlyList<(Microsoft.Extensions.Logging.LogLevel Level, string Message)> Entries { get { lock (_entries) return _entries.ToList(); } }
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(Microsoft.Extensions.Logging.LogLevel logLevel) => true;
    public void Log<TState>(Microsoft.Extensions.Logging.LogLevel logLevel, Microsoft.Extensions.Logging.EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) {
      lock (_entries) _entries.Add((logLevel, formatter(state, exception)));
    }
  }

  private static CommandSequence BuildSequence(WaitForImageConfig waitConfig) {
    var sequence = new CommandSequence {
      Id = "wait-sequence",
      Name = "Wait Sequence"
    };

    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "wait-step",
        CommandId = "wait-step",
        Action = new SequenceActionPayload {
          Type = "WaitForImage",
          Parameters = {
            ["timeoutMs"] = waitConfig.TimeoutMs,
          }
        },
        WaitForImage = waitConfig
      },
      new SequenceStep {
        Order = 1,
        StepId = "after-wait",
        CommandId = "cmd-after-wait",
        Action = new SequenceActionPayload {
          Type = "command",
          Parameters = { ["commandId"] = "cmd-after-wait" }
        }
      }
    });

    if (waitConfig.DetectionTarget is not null) {
      sequence.Steps[0].Action!.Parameters["detectionTarget"] = System.Text.Json.JsonSerializer.SerializeToElement(new {
        referenceImageId = waitConfig.DetectionTarget.ReferenceImageId,
        confidence = waitConfig.DetectionTarget.Confidence
      });
    }

    return sequence;
  }

  // ---- Feature 186: parameter placeholders in the image field ----

  private static readonly string[] QuestValues = { "pns-showdown-tile", "pns-city-anchor" };

  private static readonly GameBot.Domain.Parameters.ParameterDeclaration QuestImages = new() {
    Name = "q",
    Kind = GameBot.Domain.Parameters.ParameterInputKind.Image,
    AllowedImages = QuestValues,
    Multiple = true
  };

  private static GameBot.Domain.Parameters.ParameterScope PlaceholderScope() {
    var bindings = new System.Collections.ObjectModel.Collection<GameBot.Domain.Parameters.ParameterBinding> {
      new() { Name = "s", Value = "pns-city-anchor" },
      new() {
        Name = "q",
        Value = GameBot.Domain.Parameters.ParameterListRules.Join(QuestValues),
        Values = QuestValues
      }
    };
    return GameBot.Domain.Parameters.ParameterScope.Empty.Child(
      GameBot.Domain.Parameters.ParameterScopeLayers.Entry, bindings, new[] { QuestImages });
  }

  private static WaitForImageConfig PlaceholderConfig(string imageId) =>
    new() { TimeoutMs = 120, DetectionTarget = new DetectionTarget(imageId, 0.85) };

  private static Task<GameBot.Domain.Services.SequenceExecutionResult> RunWithScopeAsync(
      SequenceRunner runner,
      Func<GameBot.Domain.Commands.Blocks.Condition, CancellationToken, Task<bool>>? evaluator,
      bool dryRun = false) =>
    runner.ExecuteAsync(
      "wait-sequence",
      (_, _) => Task.CompletedTask,
      conditionEvaluator: evaluator,
      scope: PlaceholderScope(),
      dryRun: dryRun,
      ct: CancellationToken.None);

  [Fact]
  public async Task SinglePlaceholderResolvesAndDetectsTheImage() {
    var asked = new List<string>();
    var runner = CreateRunner(PlaceholderConfig("{{s}}"));

    var result = await RunWithScopeAsync(runner, (c, _) => {
      asked.Add(c.TargetId);
      return Task.FromResult(c.TargetId == "pns-city-anchor");
    }).ConfigureAwait(false);

    result.Steps[0].ActionOutcome.Should().Be("image_detected");
    result.Steps[0].WaitForImageDetails!.ReferenceImageId.Should().Be("pns-city-anchor");
    asked.Should().OnlyContain(id => id == "pns-city-anchor");
  }

  [Fact]
  public async Task UnknownPlaceholderFailsTheStepAndStopsTheSequence() {
    var executed = new List<string>();
    var runner = CreateRunner(PlaceholderConfig("{{missing}}"));
    var evaluated = false;

    var result = await runner.ExecuteAsync(
      "wait-sequence",
      (commandId, _) => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => {
        evaluated = true;
        return Task.FromResult(true);
      },
      scope: PlaceholderScope(),
      ct: CancellationToken.None).ConfigureAwait(false);

    result.Status.Should().Be("Failed");
    result.Steps.Should().ContainSingle();
    result.Steps[0].Status.Should().Be("Failed");
    result.Steps[0].ActionOutcome.Should().Be("failed");
    result.Steps[0].Message.Should().Contain("missing");
    executed.Should().BeEmpty();
    evaluated.Should().BeFalse();
  }

  [Fact]
  public async Task DryRunWithUnknownPlaceholderGivesTheSameError() {
    var runner = CreateRunner(PlaceholderConfig("{{missing}}"));

    var result = await RunWithScopeAsync(runner, (_, _) => Task.FromResult(true), dryRun: true).ConfigureAwait(false);

    result.Status.Should().Be("Failed");
    result.Steps[0].Message.Should().Contain("missing");
  }

  [Fact]
  public async Task DryRunWithValidPlaceholderIsSkipped() {
    var runner = CreateRunner(PlaceholderConfig("{{s}}"));

    var result = await RunWithScopeAsync(runner, (_, _) => Task.FromResult(true), dryRun: true).ConfigureAwait(false);

    result.Status.Should().Be("Succeeded");
    result.Steps[0].ActionOutcome.Should().Be(DryRunOutcomes.SkippedDryRun);
  }

  [Fact]
  public async Task MultiValuePlaceholderDetectsAnyListedImage() {
    var runner = CreateRunner(PlaceholderConfig("{{q}}"));

    var result = await RunWithScopeAsync(runner, (c, _) => Task.FromResult(c.TargetId == "pns-city-anchor")).ConfigureAwait(false);

    result.Steps[0].ActionOutcome.Should().Be("image_detected");
    result.Steps[0].WaitForImageDetails!.ReferenceImageId.Should().Be("pns-showdown-tile");
    result.Steps[0].WaitForImageDetails!.MatchedImageId.Should().Be("pns-city-anchor");
  }

  [Fact]
  public async Task PlaceholderImageNotOnTheScreenGivesTimeout() {
    var runner = CreateRunner(PlaceholderConfig("{{s}}"));

    var result = await RunWithScopeAsync(runner, (_, _) => Task.FromResult(false)).ConfigureAwait(false);

    result.Steps[0].ActionOutcome.Should().Be("timeout_elapsed");
  }

  [Fact]
  public async Task PlaceholderImageThatCannotBeLoadedGivesImageUnavailable() {
    var runner = CreateRunner(PlaceholderConfig("{{s}}"));

    var result = await RunWithScopeAsync(runner, (_, _) => throw new InvalidOperationException("image unavailable")).ConfigureAwait(false);

    result.Steps[0].ActionOutcome.Should().Be("image_unavailable");
  }

  [Fact]
  public async Task SingleImageDetectedSetsMatchedImageIdToTheResolvedId() {
    var runner = CreateRunner(PlaceholderConfig("{{s}}"));

    var result = await RunWithScopeAsync(runner, (_, _) => Task.FromResult(true)).ConfigureAwait(false);

    result.Steps[0].WaitForImageDetails!.MatchedImageId.Should().Be("pns-city-anchor");
  }

  private static SequenceRunner CreateRunner(WaitForImageConfig waitConfig) {
    return new SequenceRunner(
      new StubRepo(BuildSequence(waitConfig)),
      NullLogger<SequenceRunner>.Instance,
      new AppConfig { CaptureIntervalMs = 10 });
  }
}
