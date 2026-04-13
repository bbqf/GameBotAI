using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Unit tests for loop-step validation rules introduced in T007 / FR-002a, FR-004,
/// FR-006, FR-008, FR-012.
/// </summary>
public sealed class LoopValidationTests
{
    private static readonly SequenceStepValidationService Svc = new();

    private static SequenceStep ActionStep(string stepId, int order = 0)
        => new()
        {
            Order = order,
            StepId = stepId,
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" }
        };

    private static SequenceStep LoopStep(string stepId, LoopConfig loop, IReadOnlyList<SequenceStep>? body = null)
        => new()
        {
            Order = 0,
            StepId = stepId,
            StepType = SequenceStepType.Loop,
            Loop = loop,
            Body = body ?? Array.Empty<SequenceStep>()
        };

    // ──────────────────────────────────────────────────────────────────────────
    // 1. Top-level break is rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TopLevelBreakStepIsRejected()
    {
        var steps = new List<SequenceStep>
        {
            new() { Order = 0, StepId = "brk", StepType = SequenceStepType.Break }
        };

        var errors = Svc.Validate(steps);

        errors.Should().ContainSingle(e => e.Contains("only valid inside a loop body"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 2. Loop missing config is rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoopStepWithoutConfigIsRejected()
    {
        var step = new SequenceStep
        {
            Order = 0,
            StepId = "loop",
            StepType = SequenceStepType.Loop,
            Loop = null
        };

        var errors = Svc.Validate(new[] { step });

        errors.Should().ContainSingle(e => e.Contains("requires a loop configuration"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 3. FR-004: Count < 0 is rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void CountLoopNegativeCountIsRejected()
    {
        var step = LoopStep("loop", new CountLoopConfig { Count = -1 });
        var errors = Svc.Validate(new[] { step });
        errors.Should().ContainSingle(e => e.Contains("count must be zero or greater"));
    }

    [Fact]
    public void CountLoopZeroCountIsAccepted()
    {
        var step = LoopStep("loop", new CountLoopConfig { Count = 0 });
        var errors = Svc.Validate(new[] { step });
        errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 4. FR-008: MaxIterations <= 0 is rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoopMaxIterationsZeroIsRejected()
    {
        var step = LoopStep("loop", new WhileLoopConfig
        {
            Condition = new ImageVisibleStepCondition { ImageId = "img" },
            MaxIterations = 0
        });
        var errors = Svc.Validate(new[] { step });
        errors.Should().ContainSingle(e => e.Contains("maxIterations must be greater than zero"));
    }

    [Fact]
    public void LoopMaxIterationsNegativeIsRejected()
    {
        var step = LoopStep("loop", new CountLoopConfig { Count = 2, MaxIterations = -5 });
        var errors = Svc.Validate(new[] { step });
        errors.Should().ContainSingle(e => e.Contains("maxIterations must be greater than zero"));
    }

    [Fact]
    public void LoopMaxIterationsPositiveIsAccepted()
    {
        var step = LoopStep("loop", new CountLoopConfig { Count = 2, MaxIterations = 100 });
        var errors = Svc.Validate(new[] { step });
        errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 5. FR-012: Nested loops are rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void NestedLoopIsRejected()
    {
        var innerLoop = LoopStep("inner-loop", new CountLoopConfig { Count = 2 });
        var outer = LoopStep("outer-loop", new CountLoopConfig { Count = 3 }, new[] { innerLoop });

        var errors = Svc.Validate(new[] { outer });

        errors.Should().ContainSingle(e => e.Contains("must not itself be a loop step"));
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 6. FR-002a: {{template}} placeholder at top level is rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void TopLevelStepWithTemplatePlaceholderIsRejected()
    {
        var action = new SequenceActionPayload { Type = "tap" };
        action.Parameters["x"] = "{{iteration}}";
        var step = new SequenceStep
        {
            Order = 0,
            StepId = "s1",
            StepType = SequenceStepType.Action,
            Action = action
        };

        var errors = Svc.Validate(new[] { step });

        errors.Should().ContainSingle(e => e.Contains("only valid inside a loop body"));
    }

    [Fact]
    public void LoopBodyStepWithTemplatePlaceholderIsAccepted()
    {
        var action = new SequenceActionPayload { Type = "tap" };
        action.Parameters["x"] = "{{iteration}}";
        var bodyStep = new SequenceStep
        {
            Order = 0,
            StepId = "inner",
            StepType = SequenceStepType.Action,
            Action = action
        };
        var loop = LoopStep("loop", new CountLoopConfig { Count = 3 }, new[] { bodyStep });

        var errors = Svc.Validate(new[] { loop });

        errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 7. FR-006: commandOutcome forward-ref inside loop body is rejected
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void LoopBodyCommandOutcomeForwardRefIsRejected()
    {
        // "first" references "second" which comes after it — forward ref
        var first = new SequenceStep
        {
            Order = 0,
            StepId = "first",
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" },
            Condition = new CommandOutcomeStepCondition
            {
                StepRef = "second",
                ExpectedState = "success"
            }
        };
        var second = ActionStep("second", 1);
        var loop = LoopStep("loop", new CountLoopConfig { Count = 2 }, new[] { first, second });

        var errors = Svc.Validate(new[] { loop });

        errors.Should().ContainSingle(e => e.Contains("must reference a prior step"));
    }

    [Fact]
    public void LoopBodyCommandOutcomeBackRefIsAccepted()
    {
        var first = ActionStep("first", 0);
        var second = new SequenceStep
        {
            Order = 1,
            StepId = "second",
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" },
            Condition = new CommandOutcomeStepCondition
            {
                StepRef = "first",
                ExpectedState = "success"
            }
        };
        var loop = LoopStep("loop", new CountLoopConfig { Count = 2 }, new[] { first, second });

        var errors = Svc.Validate(new[] { loop });

        errors.Should().BeEmpty();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // 8. Valid complete loop passes
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ValidCountLoopWithBodyPasses()
    {
        var loop = LoopStep("loop", new CountLoopConfig { Count = 5 },
            new[] { ActionStep("s1"), ActionStep("s2", 1) });
        var errors = Svc.Validate(new[] { ActionStep("pre"), loop });
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidWhileLoopWithBreakPasses()
    {
        var breakStep = new SequenceStep
        {
            Order = 1,
            StepId = "brk",
            StepType = SequenceStepType.Break,
            BreakCondition = new ImageVisibleStepCondition { ImageId = "img" }
        };
        var loop = LoopStep("loop", new WhileLoopConfig
        {
            Condition = new ImageVisibleStepCondition { ImageId = "img" }
        }, new[] { ActionStep("s1"), breakStep });

        var errors = Svc.Validate(new[] { loop });
        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidRepeatUntilLoopPasses()
    {
        var loop = LoopStep("loop", new RepeatUntilLoopConfig
        {
            Condition = new ImageVisibleStepCondition { ImageId = "img" }
        }, new[] { ActionStep("s1") });
        var errors = Svc.Validate(new[] { loop });
        errors.Should().BeEmpty();
    }
}
