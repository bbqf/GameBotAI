using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Unit tests for loop step execution: count (T009), while (T018), repeat-until (T020),
/// and break (T022) covering all story-acceptance criteria.
/// </summary>
public sealed class SequenceRunnerLoopTests
{
    // ──────────────────────────────────────────────────────────────────────────
    // Infrastructure
    // ──────────────────────────────────────────────────────────────────────────

    private sealed class StubRepo : ISequenceRepository
    {
        private readonly CommandSequence _seq;
        public StubRepo(CommandSequence seq) { _seq = seq; }
        public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_seq);
        public Task<IReadOnlyList<CommandSequence>> ListAsync() =>
            Task.FromResult<IReadOnlyList<CommandSequence>>(new[] { _seq }.ToList().AsReadOnly());
        public Task<CommandSequence> CreateAsync(CommandSequence s) => Task.FromResult(s);
        public Task<CommandSequence> UpdateAsync(CommandSequence s) => Task.FromResult(s);
        public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
    }

    private static CommandSequence Sequence(string id, IEnumerable<SequenceStep> steps)
    {
        var seq = new CommandSequence { Id = id, Name = id };
        seq.SetSteps(steps.ToList());
        return seq;
    }

    private static SequenceStep ActionBodyStep(int order, string stepId, string commandId = "inner-cmd")
        => new()
        {
            Order = order,
            StepId = stepId,
            CommandId = commandId,
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" }
        };

    // ──────────────────────────────────────────────────────────────────────────
    // T009: Count loop
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountLoopExecutesBodyExactlyNTimes()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 5 },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s", id => { executed.Add(id); return Task.CompletedTask; });

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(5);
    }

    [Fact]
    public async Task CountLoopZeroCountSkipsBodyAndSucceeds()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 0 },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s", id => { executed.Add(id); return Task.CompletedTask; });

        result.Status.Should().Be("Succeeded");
        executed.Should().BeEmpty();
    }

    [Fact]
    public async Task CountLoopIterationPlaceholderSubstitutesCommandId()
    {
        // Body step uses {{iteration}} in CommandId so we can verify substitution
        var bodyStep = new SequenceStep
        {
            Order = 0,
            StepId = "inner",
            CommandId = "cmd-{{iteration}}",
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" }
        };
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 3 },
            Body = new List<SequenceStep> { bodyStep }
        };

        var executed = new List<string>();
        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s", id => { executed.Add(id); return Task.CompletedTask; });

        result.Status.Should().Be("Succeeded");
        executed.Should().Equal("cmd-1", "cmd-2", "cmd-3");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T018: While loop
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhileLoopConditionTrueTwiceThenFalseBodyRunsTwice()
    {
        var executed = new List<string>();
        var evalCount = 0;
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new WhileLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" }
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        // Condition: true, true, false  (evaluated before iteration 1, 2, 3)
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (cond, _) => Task.FromResult(++evalCount <= 2));

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(2);
    }

    [Fact]
    public async Task WhileLoopConditionFalseOnEntryBodySkipped()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new WhileLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" }
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(false));

        result.Status.Should().Be("Succeeded");
        executed.Should().BeEmpty();
        result.Steps.Should().ContainSingle();
        result.Steps[0].Status.Should().Be("Skipped");
    }

    [Fact]
    public async Task WhileLoopConditionNeverFalseFailsAtLimit()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new WhileLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" },
                MaxIterations = 3
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(true));

        result.Status.Should().Be("Failed");
        executed.Should().HaveCount(3);
    }

    [Fact]
    public async Task WhileLoopConditionThrowsLoopFails()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new WhileLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" }
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => throw new InvalidOperationException("eval error"));

        result.Status.Should().Be("Failed");
        executed.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T020: Repeat-until loop
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RepeatUntilLoopExitConditionTrueAfterIteration1BodyRunsOnce()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new RepeatUntilLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" }
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        // Condition true on first check (after first iteration)
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(true));

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(1);
    }

    [Fact]
    public async Task RepeatUntilLoopExitConditionTrueAfterIteration3BodyRuns3Times()
    {
        var executed = new List<string>();
        var evalCount = 0;
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new RepeatUntilLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" }
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        // Condition: false, false, true
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(++evalCount >= 3));

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(3);
    }

    [Fact]
    public async Task RepeatUntilLoopConditionNeverTrueFailsAtLimit()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new RepeatUntilLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" },
                MaxIterations = 3
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(false));

        result.Status.Should().Be("Failed");
        executed.Should().HaveCount(3);
    }

    [Fact]
    public async Task RepeatUntilLoopConditionThrowsAfterFirstIterationLoopFails()
    {
        var executed = new List<string>();
        var firstEval = true;
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new RepeatUntilLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img" }
            },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner") }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        // Execute once, then throw on condition eval
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) =>
            {
                if (firstEval) { firstEval = false; throw new InvalidOperationException("eval error"); }
                return Task.FromResult(false);
            });

        result.Status.Should().Be("Failed");
        executed.Should().HaveCount(1);
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T022: Break steps
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountLoopConditionalBreakOnIteration3LoopExitsAfter3Iterations()
    {
        var executed = new List<string>();
        var evalCount = 0;
        var breakStep = new SequenceStep
        {
            Order = 1,
            StepId = "break",
            StepType = SequenceStepType.Break,
            BreakCondition = new ImageVisibleStepCondition { ImageId = "img" }
        };
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 10 },
            Body = new List<SequenceStep> { ActionBodyStep(0, "inner"), breakStep }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        // Break condition returns true at evalCount == 3 (on 3rd iteration's break check)
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(++evalCount >= 3));

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(3);

        // Break was triggered — should be recorded in loop step result
        var loopStepResult = result.Steps.FirstOrDefault(s => s.LoopIterations is not null);
        loopStepResult.Should().NotBeNull();
        var lastIter = loopStepResult!.LoopIterations![^1];
        lastIter.BreakTriggered.Should().BeTrue();
    }

    [Fact]
    public async Task CountLoopConditionalBreakNeverTriggeredLoopRunsToCompletion()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 5 },
            Body = new List<SequenceStep>
            {
                ActionBodyStep(0, "inner"),
                new SequenceStep
                {
                    Order = 1,
                    StepId = "break",
                    StepType = SequenceStepType.Break,
                    BreakCondition = new ImageVisibleStepCondition { ImageId = "img" }
                }
            }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        // Break condition never true
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => Task.FromResult(false));

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(5);
    }

    [Fact]
    public async Task CountLoopUnconditionalBreakExitsAfterFirstIteration()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 10 },
            Body = new List<SequenceStep>
            {
                ActionBodyStep(0, "inner"),
                new SequenceStep
                {
                    Order = 1,
                    StepId = "break",
                    StepType = SequenceStepType.Break,
                    BreakCondition = null // unconditional
                }
            }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; });

        result.Status.Should().Be("Succeeded");
        executed.Should().HaveCount(1);
    }

    [Fact]
    public async Task CountLoopBreakConditionThrowsLoopFails()
    {
        var executed = new List<string>();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 10 },
            Body = new List<SequenceStep>
            {
                ActionBodyStep(0, "inner"),
                new SequenceStep
                {
                    Order = 1,
                    StepId = "break",
                    StepType = SequenceStepType.Break,
                    BreakCondition = new ImageVisibleStepCondition { ImageId = "img" }
                }
            }
        };

        var runner = new SequenceRunner(new StubRepo(Sequence("s", new[] { loopStep })));
        var result = await runner.ExecuteAsync("s",
            id => { executed.Add(id); return Task.CompletedTask; },
            conditionEvaluator: (_, _) => throw new InvalidOperationException("eval error"));

        result.Status.Should().Be("Failed");
    }
}
