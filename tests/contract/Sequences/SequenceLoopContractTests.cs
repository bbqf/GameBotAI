using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

namespace GameBot.ContractTests.Sequences;

/// <summary>
/// Verifies that the loop and break step types survive a full persist → load round-trip
/// through <see cref="FileSequenceRepository"/>.  Attribute-based JSON polymorphism
/// (<c>[JsonPolymorphic]</c> / <c>[JsonDerivedType]</c>) must correctly reconstruct
/// <see cref="LoopConfig"/>, <see cref="SequenceStepCondition"/>, and nested body steps.
/// </summary>
public sealed class SequenceLoopContractTests : IDisposable
{
    private readonly string _tmpRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    private FileSequenceRepository CreateRepo() => new(_tmpRoot);

    public void Dispose()
    {
        if (Directory.Exists(_tmpRoot))
        {
            Directory.Delete(_tmpRoot, recursive: true);
        }
    }

    // ──────────────────────────────────────────────────────────────────────────
    // Helpers
    // ──────────────────────────────────────────────────────────────────────────

    private static CommandSequence BuildSequence(string name, IEnumerable<SequenceStep> steps)
    {
        var seq = new CommandSequence
        {
            Id = string.Empty,
            Name = name,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
        seq.SetSteps(steps.ToList());
        return seq;
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T008-1: count loop with inner action step
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CountLoopStepRoundTripsViaRepository()
    {
        var repo = CreateRepo();
        var bodyStep = new SequenceStep
        {
            Order = 0,
            StepId = "inner-tap",
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" }
        };
        bodyStep.Action.Parameters["x"] = 100;
        bodyStep.Action.Parameters["y"] = "{{iteration}}";

        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "count-loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 5 },
            Body = new List<SequenceStep> { bodyStep }
        };

        var created = await repo.CreateAsync(BuildSequence("count-loop-rt", new[] { loopStep }));
        var retrieved = await repo.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        var step = retrieved!.Steps.Single();
        step.StepType.Should().Be(SequenceStepType.Loop);
        step.Loop.Should().BeOfType<CountLoopConfig>();
        ((CountLoopConfig)step.Loop!).Count.Should().Be(5);
        step.Body.Should().HaveCount(1);
        step.Body[0].StepId.Should().Be("inner-tap");
        step.Body[0].Action!.Parameters["y"]!.ToString().Should().Be("{{iteration}}");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T008-2: while loop with imageVisible condition
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WhileLoopStepRoundTripsViaRepository()
    {
        var repo = CreateRepo();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "while-loop",
            StepType = SequenceStepType.Loop,
            Loop = new WhileLoopConfig
            {
                Condition = new ImageVisibleStepCondition { ImageId = "img-001", MinSimilarity = 0.85 },
                MaxIterations = 10
            },
            Body = new List<SequenceStep>
            {
                new SequenceStep
                {
                    Order = 0,
                    StepId = "tap-step",
                    StepType = SequenceStepType.Action,
                    Action = new SequenceActionPayload { Type = "tap" }
                }
            }
        };

        var created = await repo.CreateAsync(BuildSequence("while-loop-rt", new[] { loopStep }));
        var retrieved = await repo.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        var step = retrieved!.Steps.Single();
        step.Loop.Should().BeOfType<WhileLoopConfig>();
        var whileCfg = (WhileLoopConfig)step.Loop!;
        whileCfg.MaxIterations.Should().Be(10);
        whileCfg.Condition.Should().BeOfType<ImageVisibleStepCondition>();
        ((ImageVisibleStepCondition)whileCfg.Condition).ImageId.Should().Be("img-001");
        ((ImageVisibleStepCondition)whileCfg.Condition).MinSimilarity.Should().Be(0.85);
        step.Body.Single().StepId.Should().Be("tap-step");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T008-3: repeatUntil loop with commandOutcome condition
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RepeatUntilLoopStepRoundTripsViaRepository()
    {
        var repo = CreateRepo();
        var bodyStep = new SequenceStep
        {
            Order = 0,
            StepId = "do-action",
            StepType = SequenceStepType.Action,
            Action = new SequenceActionPayload { Type = "tap" }
        };

        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "repeat-loop",
            StepType = SequenceStepType.Loop,
            Loop = new RepeatUntilLoopConfig
            {
                Condition = new CommandOutcomeStepCondition { StepRef = "do-action", ExpectedState = "success" }
            },
            Body = new List<SequenceStep> { bodyStep }
        };

        var created = await repo.CreateAsync(BuildSequence("repeat-until-rt", new[] { loopStep }));
        var retrieved = await repo.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        var step = retrieved!.Steps.Single();
        step.Loop.Should().BeOfType<RepeatUntilLoopConfig>();
        var ruCfg = (RepeatUntilLoopConfig)step.Loop!;
        ruCfg.Condition.Should().BeOfType<CommandOutcomeStepCondition>();
        var cond = (CommandOutcomeStepCondition)ruCfg.Condition;
        cond.StepRef.Should().Be("do-action");
        cond.ExpectedState.Should().Be("success");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T008-4a: unconditional break step
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task UnconditionalBreakStepRoundTripsViaRepository()
    {
        var repo = CreateRepo();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "count-loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 10 },
            Body = new List<SequenceStep>
            {
                new SequenceStep
                {
                    Order = 0,
                    StepId = "break-step",
                    StepType = SequenceStepType.Break,
                    BreakCondition = null   // unconditional
                }
            }
        };

        var created = await repo.CreateAsync(BuildSequence("unconditional-break-rt", new[] { loopStep }));
        var retrieved = await repo.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        var breakStep = retrieved!.Steps.Single().Body.Single();
        breakStep.StepType.Should().Be(SequenceStepType.Break);
        breakStep.BreakCondition.Should().BeNull();
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T008-4b: conditional break step
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConditionalBreakStepRoundTripsViaRepository()
    {
        var repo = CreateRepo();
        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "count-loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 10 },
            Body = new List<SequenceStep>
            {
                new SequenceStep
                {
                    Order = 0,
                    StepId = "break-step",
                    StepType = SequenceStepType.Break,
                    BreakCondition = new ImageVisibleStepCondition { ImageId = "exit-image" }
                }
            }
        };

        var created = await repo.CreateAsync(BuildSequence("conditional-break-rt", new[] { loopStep }));
        var retrieved = await repo.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        var breakStep = retrieved!.Steps.Single().Body.Single();
        breakStep.StepType.Should().Be(SequenceStepType.Break);
        breakStep.BreakCondition.Should().BeOfType<ImageVisibleStepCondition>();
        ((ImageVisibleStepCondition)breakStep.BreakCondition!).ImageId.Should().Be("exit-image");
    }

    // ──────────────────────────────────────────────────────────────────────────
    // T008-5: body inner steps preserve order and stepId values
    // ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoopBodyStepsPreserveOrderAndStepIds()
    {
        var repo = CreateRepo();
        var bodySteps = Enumerable.Range(0, 4)
            .Select(i => new SequenceStep
            {
                Order = i,
                StepId = $"inner-{i}",
                StepType = SequenceStepType.Action,
                Action = new SequenceActionPayload { Type = "tap" }
            })
            .ToList();

        var loopStep = new SequenceStep
        {
            Order = 0,
            StepId = "multi-body-loop",
            StepType = SequenceStepType.Loop,
            Loop = new CountLoopConfig { Count = 3 },
            Body = bodySteps
        };

        var created = await repo.CreateAsync(BuildSequence("body-order-rt", new[] { loopStep }));
        var retrieved = await repo.GetAsync(created.Id);

        retrieved.Should().NotBeNull();
        var body = retrieved!.Steps.Single().Body;
        body.Should().HaveCount(4);
        for (var i = 0; i < 4; i++)
        {
            body[i].Order.Should().Be(i);
            body[i].StepId.Should().Be($"inner-{i}");
        }
    }
}
