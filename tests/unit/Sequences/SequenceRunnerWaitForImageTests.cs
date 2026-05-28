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
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(++invocationCount >= 3),
      ct: CancellationToken.None);

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
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => Task.FromResult(false),
      ct: CancellationToken.None);
    stopwatch.Stop();

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-after-wait");
    result.Steps.Should().ContainSingle(step => step.ActionOutcome == "timeout_elapsed");
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
      commandId => {
        executed.Add(commandId);
        return Task.CompletedTask;
      },
      conditionEvaluator: (_, _) => throw new InvalidOperationException("image unavailable"),
      ct: CancellationToken.None);
    stopwatch.Stop();

    result.Status.Should().Be("Succeeded");
    executed.Should().ContainSingle().Which.Should().Be("cmd-after-wait");
    result.Steps.Should().ContainSingle(step => step.ActionOutcome == "image_unavailable");
    stopwatch.ElapsedMilliseconds.Should().BeGreaterThanOrEqualTo(25);
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