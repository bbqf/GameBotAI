using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

// Test-code analyzer relaxations (permitted by the constitution for test code).
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 114: the runner resolves a placeholder in <c>imageVisible.imageId</c> against the scope of
/// each call site before the evaluation, in each condition position.
/// </summary>
public sealed class SequenceRunnerConditionScopeTests {
  private static string Unresolved(string fieldPath) =>
      $"parameter 'novaOption' used by field '{fieldPath}' could not be resolved from any scope.";

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  /// <summary>What one run did: the image ids the evaluator got, and the device inputs.</summary>
  private sealed record RunRecord(SequenceExecutionResult Result, List<string> ImageIds, int Dispatches);

  private static ImageVisibleStepCondition Image(string id) => new() { ImageId = id };

  private static SequenceStep Tap(string stepId, int order = 0, SequenceStepCondition? condition = null) => new() {
    Order = order,
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 2 } },
    Condition = condition
  };

  private static ParameterScope EntryScope(string? novaOption) {
    var bindings = new Collection<ParameterBinding>();
    if (novaOption is not null) bindings.Add(new ParameterBinding { Name = "novaOption", Value = novaOption });
    return ParameterScope.Empty.Child(ParameterScopeLayers.Entry, bindings, null);
  }

  private static async Task<RunRecord> RunAsync(
      IEnumerable<SequenceStep> steps,
      string? novaOption,
      Func<int, bool>? answer = null) {
    var sequence = new CommandSequence { Id = "seq", Name = "Seq" };
    sequence.SetSteps(steps.ToArray());
    var runner = new SequenceRunner(new StubRepo(sequence));
    var imageIds = new List<string>();
    var dispatches = 0;

    var result = await runner.ExecuteAsync(
      sequence.Id,
      (_, _) => Task.CompletedTask,
      conditionEvaluator: (condition, _) => {
        imageIds.Add(condition.TargetId ?? string.Empty);
        return Task.FromResult(answer?.Invoke(imageIds.Count) ?? true);
      },
      actionDispatcher: (_, _) => {
        dispatches++;
        return Task.FromResult(new ActionDispatchResult("executed", null));
      },
      scope: EntryScope(novaOption),
      ct: CancellationToken.None);

    return new RunRecord(result, imageIds, dispatches);
  }

  // ── Resolved id in each position ──────────────────────────────────────────

  [Fact]
  public async Task ActionStepGuardUsesTheResolvedImageId() {
    var run = await RunAsync(new[] { Tap("tap-option", condition: Image("{{novaOption}}")) }, "option-b");

    run.ImageIds.Should().Equal("option-b");
    run.Dispatches.Should().Be(1);
    run.Result.Status.Should().Be("Succeeded");
  }

  [Fact]
  public async Task LoopStepGuardUsesTheResolvedImageId() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 },
      Condition = Image("{{novaOption}}"),
      Body = new[] { Tap("body-tap") }
    };

    var run = await RunAsync(new[] { loop }, "option-b");

    run.ImageIds.Should().Equal("option-b");
    run.Dispatches.Should().Be(1);
  }

  [Fact]
  public async Task StepGuardInALoopBodyUsesTheIterationScope() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new[] { Tap("body-tap", condition: Image("img-{{iteration}}")) }
    };

    var run = await RunAsync(new[] { loop }, null);

    run.ImageIds.Should().Equal("img-1", "img-2");
  }

  [Fact]
  public async Task IfConditionUsesTheResolvedImageIdAndLogsIt() {
    var ifStep = new SequenceStep {
      Order = 0, StepId = "if1", StepType = SequenceStepType.If,
      If = new IfConfig { Condition = Image("{{novaOption}}") },
      Body = new[] { Tap("then-tap") }
    };

    var run = await RunAsync(new[] { ifStep }, "option-b");

    run.ImageIds.Should().Equal("option-b");
    run.Result.Steps.Should().Contain(s => s.Message != null
        && s.Message.Contains("imageVisible(imageId=option-b, minSimilarity=default)", StringComparison.Ordinal));
  }

  [Fact]
  public async Task WhileConditionGetsTheIterationThatIsAboutToRun() {
    var loop = new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Image("img-{{iteration}}"), MaxIterations = 5 },
      Body = new[] { Tap("body-tap") }
    };

    // True for the first two evaluations, then false.
    var run = await RunAsync(new[] { loop }, null, answer: count => count <= 2);

    run.ImageIds.Should().Equal("img-1", "img-2", "img-3");
    run.Dispatches.Should().Be(2);
  }

  [Fact]
  public async Task RepeatUntilConditionGetsTheIterationThatJustRan() {
    var loop = new SequenceStep {
      Order = 0, StepId = "r1", StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig { Condition = Image("img-{{iteration}}"), MaxIterations = 5 },
      Body = new[] { Tap("body-tap") }
    };

    // False for the first evaluation, then true.
    var run = await RunAsync(new[] { loop }, null, answer: count => count >= 2);

    run.ImageIds.Should().Equal("img-1", "img-2");
    run.Dispatches.Should().Be(2);
  }

  [Fact]
  public async Task BreakConditionUsesTheResolvedImageIdAndLogsIt() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 3 },
      Body = new[] {
        new SequenceStep {
          Order = 0, StepId = "brk", StepType = SequenceStepType.Break, BreakCondition = Image("{{novaOption}}")
        }
      }
    };

    var run = await RunAsync(new[] { loop }, "option-b");

    run.ImageIds.Should().Equal("option-b");
    run.Result.Steps.Should().Contain(s => s.StepId == null && s.CommandId == "brk"
        && s.Message == "Break triggered: imageVisible(imageId=option-b, minSimilarity=default) evaluated to true");
  }

  // ── Unresolved name ───────────────────────────────────────────────────────

  [Fact]
  public async Task UnresolvedNameInAStepGuardFailsTheStepWithTheExactMessage() {
    var run = await RunAsync(new[] { Tap("tap-option", condition: Image("{{novaOption}}")) }, null);

    var expected = "Step 'tap-option': " + Unresolved("condition.imageId");
    run.Result.Status.Should().Be("Failed");
    run.Result.Steps.Should().ContainSingle(s => s.Status == "Failed").Which.Message.Should().Be(expected);
    run.ImageIds.Should().BeEmpty();
    run.Dispatches.Should().Be(0);
  }

  [Fact]
  public async Task UnresolvedNameInALoopStepGuardFailsTheStepWithTheExactMessage() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 },
      Condition = Image("{{novaOption}}"),
      Body = new[] { Tap("body-tap") }
    };

    var run = await RunAsync(new[] { loop }, null);

    var expected = "Step 'loop1': " + Unresolved("condition.imageId");
    run.Result.Status.Should().Be("Failed");
    run.Result.Steps.Should().ContainSingle(s => s.Status == "Failed").Which.Message.Should().Be(expected);
    run.Dispatches.Should().Be(0);
  }

  [Fact]
  public async Task UnresolvedNameInAWhileConditionFailsTheStepWithThePrefix() {
    var loop = new SequenceStep {
      Order = 0, StepId = "w1", StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Image("{{novaOption}}"), MaxIterations = 5 },
      Body = new[] { Tap("body-tap") }
    };

    var run = await RunAsync(new[] { loop }, null);

    run.Result.Status.Should().Be("Failed");
    run.Result.Steps.Should().ContainSingle(s => s.Status == "Failed").Which.Message.Should()
        .Contain("Loop 'w1' condition evaluation failed: ")
        .And.Contain("Step 'w1': " + Unresolved("loop.condition.imageId"));
    run.Dispatches.Should().Be(0);
  }

  [Fact]
  public async Task UnresolvedNameInARepeatUntilConditionFailsTheStepWithThePrefix() {
    var loop = new SequenceStep {
      Order = 0, StepId = "r1", StepType = SequenceStepType.Loop,
      Loop = new RepeatUntilLoopConfig { Condition = Image("{{novaOption}}"), MaxIterations = 5 },
      Body = new[] { Tap("body-tap") }
    };

    var run = await RunAsync(new[] { loop }, null);

    run.Result.Status.Should().Be("Failed");
    run.Result.Steps.Should().Contain(s => s.Status == "Failed").Which.Message.Should()
        .Contain("Loop 'r1' exit condition evaluation failed: ")
        .And.Contain(Unresolved("loop.condition.imageId"));
    // The body runs one time before the first evaluation of a repeat-until condition.
    run.Dispatches.Should().Be(1);
    run.ImageIds.Should().BeEmpty();
  }

  [Fact]
  public async Task UnresolvedNameInAnIfConditionFailsTheStepWithThePrefix() {
    var ifStep = new SequenceStep {
      Order = 0, StepId = "if1", StepType = SequenceStepType.If,
      If = new IfConfig { Condition = Image("{{novaOption}}") },
      Body = new[] { Tap("then-tap") }
    };

    var run = await RunAsync(new[] { ifStep }, null);

    run.Result.Status.Should().Be("Failed");
    run.Result.Steps.Should().ContainSingle(s => s.Status == "Failed").Which.Message.Should()
        .Contain("If 'if1' condition evaluation failed: ")
        .And.Contain("Step 'if1': " + Unresolved("if.condition.imageId"));
    run.Dispatches.Should().Be(0);
  }

  [Fact]
  public async Task UnresolvedNameInABreakConditionGivesNoBreak() {
    var loop = new SequenceStep {
      Order = 0, StepId = "loop1", StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 1 },
      Body = new[] {
        new SequenceStep {
          Order = 0, StepId = "brk", StepType = SequenceStepType.Break, BreakCondition = Image("{{novaOption}}")
        }
      }
    };

    var run = await RunAsync(new[] { loop }, null);

    run.Result.Status.Should().Be("Succeeded");
    var brk = run.Result.Steps.Should().ContainSingle(s => s.CommandId == "brk").Subject;
    brk.ActionOutcome.Should().Be(BreakOutcomes.NoBreak);
    brk.ConditionResult.Should().Be("error");
    brk.Message.Should().StartWith("No break: ")
        .And.Contain("Step 'brk': " + Unresolved("breakCondition.imageId"));
    run.ImageIds.Should().BeEmpty();
  }
}
