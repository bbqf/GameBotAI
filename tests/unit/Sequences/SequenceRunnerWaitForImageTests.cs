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

  private static SequenceRunner CreateRunner(WaitForImageConfig waitConfig) {
    return new SequenceRunner(
      new StubRepo(BuildSequence(waitConfig)),
      NullLogger<SequenceRunner>.Instance,
      new AppConfig { CaptureIntervalMs = 10 });
  }
}
