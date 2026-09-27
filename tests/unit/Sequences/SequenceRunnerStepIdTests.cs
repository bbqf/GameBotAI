using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Issue #221 (B-020): two or more steps can use the same command. Each command step result must
/// have the StepId of the step that ran, so that the execution log can name the correct step.
/// </summary>
public sealed class SequenceRunnerStepIdTests {
  private const string SharedCommand = "cmd-shared";

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _seq;
    public StubRepo(CommandSequence seq) { _seq = seq; }
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
        Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _seq }.ToList().AsReadOnly());
    public Task<CommandSequence> CreateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceRunner Runner(params SequenceStep[] steps) {
    var seq = new CommandSequence { Id = "s", Name = "s" };
    seq.SetSteps(steps.ToList());
    return new SequenceRunner(new StubRepo(seq));
  }

  private static SequenceStep CommandStep(int order, string stepId, SequenceStepCondition? condition = null)
      => new() {
        Order = order,
        StepId = stepId,
        CommandId = SharedCommand,
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = "tap" },
        Condition = condition
      };

  private static SequenceStep IfStep(
      int order,
      string stepId,
      bool conditionResult,
      IReadOnlyList<SequenceStep> body,
      IReadOnlyList<SequenceStep>? elseBody = null)
      => new() {
        Order = order,
        StepId = stepId,
        StepType = SequenceStepType.If,
        // The test evaluator returns true for "img-true" and false for all other images.
        If = new IfConfig { Condition = new ImageVisibleStepCondition { ImageId = conditionResult ? "img-true" : "img-false" } },
        Body = body,
        ElseBody = elseBody
      };

  private static readonly Func<Condition, CancellationToken, Task<bool>> EvaluateByImageId =
      (condition, _) => Task.FromResult(string.Equals(condition.TargetId, "img-true", StringComparison.Ordinal));

  private static IEnumerable<string?> CommandStepIds(SequenceExecutionResult result)
      => result.Steps.Where(s => s.CommandId == SharedCommand).Select(s => s.StepId);

  [Fact]
  public async Task TopLevelStepsThatShareACommandEachRecordTheirOwnStepId() {
    var runner = Runner(CommandStep(0, "a"), CommandStep(1, "b"), CommandStep(2, "c"));

    var result = await runner.ExecuteAsync("s", (_, _) => Task.CompletedTask);

    result.Status.Should().Be("Succeeded");
    CommandStepIds(result).Should().Equal("a", "b", "c");
  }

  [Fact]
  public async Task StepSkippedByItsGuardRecordsItsOwnStepId() {
    var runner = Runner(
        CommandStep(0, "first"),
        CommandStep(1, "guarded", new ImageVisibleStepCondition { ImageId = "img-false" }));

    var result = await runner.ExecuteAsync("s", (_, _) => Task.CompletedTask, conditionEvaluator: EvaluateByImageId);

    var skipped = result.Steps.Single(s => s.CommandId == SharedCommand && s.Status == "Skipped");
    skipped.StepId.Should().Be("guarded");
  }

  [Fact]
  public async Task FailedCommandStepRecordsItsOwnStepId() {
    var runner = Runner(CommandStep(0, "first"), CommandStep(1, "second"));
    var calls = 0;

    var result = await runner.ExecuteAsync("s", (_, _) => {
      calls++;
      return calls == 2 ? throw new InvalidOperationException("boom") : Task.CompletedTask;
    });

    result.Status.Should().Be("Failed");
    var failed = result.Steps.Single(s => s.CommandId == SharedCommand && s.Status == "Failed");
    failed.StepId.Should().Be("second");
  }

  [Fact]
  public async Task OnlyTheIfBodiesThatRanRecordACommandResult() {
    var runner = Runner(
        IfStep(0, "if-false", conditionResult: false, new[] { CommandStep(0, "body-false") }),
        IfStep(1, "if-true", conditionResult: true, new[] { CommandStep(0, "body-true") }));

    var result = await runner.ExecuteAsync("s", (_, _) => Task.CompletedTask, conditionEvaluator: EvaluateByImageId);

    result.Status.Should().Be("Succeeded");
    CommandStepIds(result).Should().Equal("body-true");
  }

  [Fact]
  public async Task ElseBranchStepRecordsItsOwnStepId() {
    var runner = Runner(
        CommandStep(0, "first"),
        IfStep(1, "if-else", conditionResult: false, Array.Empty<SequenceStep>(), new[] { CommandStep(0, "else-step") }));

    var result = await runner.ExecuteAsync("s", (_, _) => Task.CompletedTask, conditionEvaluator: EvaluateByImageId);

    result.Status.Should().Be("Succeeded");
    CommandStepIds(result).Should().Equal("first", "else-step");
  }

  [Fact]
  public async Task LoopBodyStepRecordsItsOwnStepIdInEachIteration() {
    var loop = new SequenceStep {
      Order = 1,
      StepId = "loop-1",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] { CommandStep(0, "loop-step") }
    };
    var runner = Runner(CommandStep(0, "first"), loop);

    var result = await runner.ExecuteAsync("s", (_, _) => Task.CompletedTask);

    result.Status.Should().Be("Succeeded");
    CommandStepIds(result).Should().Equal("first", "loop-step", "loop-step");
  }

  [Fact]
  public void StepIdIsNotInTheJsonOfTheRunResult() {
    var result = SequenceExecutionResult.Start("s");
    result.AddStep(SharedCommand, 0, actionOutcome: "executed", stepId: "a");

    var json = JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    using var doc = JsonDocument.Parse(json);
    var step = doc.RootElement.GetProperty("steps")[0];
    step.TryGetProperty("stepId", out _).Should().BeFalse();
    step.GetProperty("commandId").GetString().Should().Be(SharedCommand);
  }
}
