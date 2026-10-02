using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.Blocks;
using GameBot.Domain.Services;
using GameBot.Domain.Services.StepThrough;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.IntegrationTests.StepThrough;

/// <summary>
/// SC-002: the order of steps that repeated "Run next step" runs is the order of a real run with the
/// same inputs. Each fixture runs through <see cref="SequenceRunner"/> and through
/// <see cref="SequenceStepper"/> with the same answers for the image conditions. The test compares the
/// order of the commands and the order of the condition checks.
/// </summary>
public sealed class StepperParityTests {
  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;

    public StubRepo(CommandSequence sequence) { _sequence = sequence; }

    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);

    public Task<IReadOnlyList<CommandSequence>> ListAsync()
      => Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _sequence });

    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);

    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private sealed record Fixture(string Name, CommandSequence Sequence, Dictionary<string, bool[]> Answers);

  private static SequenceStep Cmd(int order, string id, SequenceStepCondition? guard = null)
    => new() {
      Order = order,
      StepId = id,
      CommandId = id,
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "tap" },
      Condition = guard
    };

  private static SequenceStep Count(int order, string id, int count, params SequenceStep[] body)
    => new() { Order = order, StepId = id, StepType = SequenceStepType.Loop, Loop = new CountLoopConfig { Count = count }, Body = body };

  private static SequenceStep While(int order, string id, int? max, bool exitOnMax, params SequenceStep[] body)
    => new() {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Img("w-" + id), MaxIterations = max, ExitOnMaxIterations = exitOnMax },
      Body = body
    };

  private static SequenceStep Until(int order, string id, params SequenceStep[] body)
    => new() { Order = order, StepId = id, StepType = SequenceStepType.Loop, Loop = new RepeatUntilLoopConfig { Condition = Img("u-" + id) }, Body = body };

  private static SequenceStep If(int order, string id, SequenceStep[] thenSteps, SequenceStep[]? elseSteps = null)
    => new() {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.If,
      If = new IfConfig { Condition = Img("i-" + id) },
      Body = thenSteps,
      ElseBody = elseSteps
    };

  private static SequenceStep Brk(int order, string id, string? imageId = null)
    => new() { Order = order, StepId = id, StepType = SequenceStepType.Break, BreakCondition = imageId is null ? null : Img(imageId) };

  private static ImageVisibleStepCondition Img(string id) => new() { ImageId = id };

  private static CommandSequence Seq(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(steps);
    return sequence;
  }

  private static Fixture Fx(string name, CommandSequence sequence, params (string Image, bool[] Values)[] answers)
    => new(name, sequence, answers.ToDictionary(a => a.Image, a => a.Values));

  private static Fixture[] Build() {
    return new[] {
      Fx("linear", Seq(Cmd(0, "a"), Cmd(1, "b"), Cmd(2, "c"))),
      Fx("out of order", Seq(Cmd(2, "c"), Cmd(0, "a"), Cmd(1, "b"))),
      Fx("count loop", Seq(Cmd(0, "pre"), Count(1, "l", 3, Cmd(0, "x"), Cmd(1, "y")), Cmd(2, "post"))),
      Fx("empty count loop", Seq(Count(0, "l", 0, Cmd(0, "x")), Cmd(1, "post"))),
      Fx("while loop", Seq(While(0, "l", null, false, Cmd(0, "x")), Cmd(1, "post")), ("w-l", new[] { true, true, true, false })),
      Fx("while false on entry", Seq(While(0, "l", null, false, Cmd(0, "x")), Cmd(1, "post")), ("w-l", new[] { false })),
      Fx("while ceiling exits", Seq(While(0, "l", 2, true, Cmd(0, "x")), Cmd(1, "post")), ("w-l", new[] { true, true, true, true })),
      Fx("repeat until", Seq(Until(0, "l", Cmd(0, "x")), Cmd(1, "post")), ("u-l", new[] { false, false, true })),
      Fx("if then", Seq(If(0, "f", new[] { Cmd(0, "t1"), Cmd(1, "t2") }, new[] { Cmd(0, "e1") }), Cmd(1, "post")), ("i-f", new[] { true })),
      Fx("if else", Seq(If(0, "f", new[] { Cmd(0, "t1") }, new[] { Cmd(0, "e1"), Cmd(1, "e2") }), Cmd(1, "post")), ("i-f", new[] { false })),
      Fx("if without else", Seq(If(0, "f", new[] { Cmd(0, "t1") }), Cmd(1, "post")), ("i-f", new[] { false })),
      Fx("if in loop", Seq(Count(0, "l", 3, If(0, "f", new[] { Cmd(0, "t") }, new[] { Cmd(0, "e") }), Cmd(1, "tail"))), ("i-f", new[] { true, false, true })),
      Fx("break in loop", Seq(Count(0, "l", 5, Cmd(0, "x"), Brk(1, "b1"), Cmd(2, "y")), Cmd(1, "post"))),
      Fx("conditional break", Seq(Count(0, "l", 4, Cmd(0, "x"), Brk(1, "b1", "bc"), Cmd(2, "y")), Cmd(1, "post")), ("bc", new[] { false, false, true })),
      Fx("break in if in loop", Seq(Count(0, "l", 4, Cmd(0, "x"), If(1, "f", new[] { Brk(0, "b1") }), Cmd(2, "y")), Cmd(1, "post")), ("i-f", new[] { false, true })),
      Fx("guarded steps", Seq(Cmd(0, "a", Img("g1")), Cmd(1, "b", Img("g2")), Cmd(2, "c")), ("g1", new[] { false }), ("g2", new[] { true })),
      Fx("nested if in if", Seq(If(0, "f", new[] { If(0, "g", new[] { Cmd(0, "deep") }, new[] { Cmd(0, "other") }) }), Cmd(1, "post")), ("i-f", new[] { true }), ("i-g", new[] { false })),
      Fx("loop after loop", Seq(Count(0, "l1", 2, Cmd(0, "x")), Count(1, "l2", 2, Cmd(0, "y")), Cmd(2, "post")))
    };
  }

  public static IEnumerable<object[]> Names() => Build().Select(f => new object[] { f.Name });

  private static Func<Condition, CancellationToken, Task<bool>> Evaluator(Fixture fixture, List<string> checks) {
    var queues = fixture.Answers.ToDictionary(p => p.Key, p => new Queue<bool>(p.Value));
    return (condition, _) => {
      var id = condition.TargetId ?? string.Empty;
      checks.Add(id);
      return Task.FromResult(queues.TryGetValue(id, out var queue) && queue.Count > 0 && queue.Dequeue());
    };
  }

  [Theory]
  [MemberData(nameof(Names))]
  public async Task StepperVisitsTheSameCommandsInTheSameOrderAsARealRun(string name) {
    var fixture = Build().Single(f => f.Name == name);
    var runner = new SequenceRunner(new StubRepo(fixture.Sequence));

    var realExecuted = new List<string>();
    var realChecks = new List<string>();
    var real = await runner.ExecuteAsync(
      "seq",
      (id, _) => { realExecuted.Add(id); return Task.CompletedTask; },
      conditionEvaluator: Evaluator(fixture, realChecks));
    real.Status.Should().Be("Succeeded", fixture.Name);

    var stepExecuted = new List<string>();
    var stepChecks = new List<string>();
    var state = new StepperState();
    state.Restart(fixture.Sequence);
    var stepper = new SequenceStepper(runner);
    var dependencies = new StepperDependencies {
      ExecuteCommandAsync = (id, _) => { stepExecuted.Add(id); return Task.CompletedTask; },
      ConditionEvaluator = Evaluator(fixture, stepChecks)
    };
    for (var i = 0; i < 500 && state.Cursor is not null; i++) {
      await stepper.RunNextAsync(fixture.Sequence, state, dependencies);
    }

    state.Cursor.Should().BeNull(fixture.Name);
    stepExecuted.Should().Equal(realExecuted, fixture.Name);
    stepChecks.Should().Equal(realChecks, fixture.Name);
  }
}
