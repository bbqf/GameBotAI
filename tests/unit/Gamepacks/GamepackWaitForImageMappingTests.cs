using System;
using System.Collections.Generic;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 177: the walk that sets the WaitForImage configuration of every inline step.</summary>
public sealed class GamepackWaitForImageMappingTests {
  private static SequenceStep Wfi(string id, int timeout = 3000) {
    var step = new SequenceStep { StepId = id, StepType = SequenceStepType.Action, Action = new SequenceActionPayload { Type = "WaitForImage" } };
    step.Action.Parameters["timeoutMs"] = JsonSerializer.Deserialize<JsonElement>(timeout.ToString());
    step.Action.Parameters["detectionTarget"] = JsonSerializer.Deserialize<JsonElement>("{\"referenceImageId\":\"img\"}");
    return step;
  }

  private static SequenceStep Container(string id, SequenceStepType type, IReadOnlyList<SequenceStep> body, IReadOnlyList<SequenceStep>? elseBody = null) =>
    new() { StepId = id, StepType = type, Body = body, ElseBody = elseBody };

  private static CommandSequence Seq(params SequenceStep[] steps) {
    var sequence = new CommandSequence { Name = "s" };
    sequence.SetSteps(steps);
    return sequence;
  }

  [Fact]
  public void TopLevelStepGetsTheConfiguration() {
    var sequence = Seq(Wfi("a"));

    GamepackWaitForImageMapping.Apply(sequence);

    sequence.Steps[0].WaitForImage!.TimeoutMs.Should().Be(3000);
    sequence.Steps[0].WaitForImage!.DetectionTarget!.ReferenceImageId.Should().Be("img");
  }

  [Fact]
  public void LoopBodyIfBodyAndElseBodyGetTheConfiguration() {
    var inLoop = Wfi("l");
    var inIf = Wfi("i");
    var inElse = Wfi("e");
    var sequence = Seq(Container("L", SequenceStepType.Loop, new[] { inLoop }), Container("I", SequenceStepType.If, new[] { inIf }, new[] { inElse }));

    GamepackWaitForImageMapping.Apply(sequence);

    inLoop.WaitForImage.Should().NotBeNull();
    inIf.WaitForImage.Should().NotBeNull();
    inElse.WaitForImage.Should().NotBeNull();
  }

  [Fact]
  public void ThreeLevelsGetTheConfiguration() {
    var deep = Wfi("d", 1234);
    var inner = Container("c", SequenceStepType.Loop, new[] { deep });
    var middle = Container("b", SequenceStepType.If, new[] { inner });
    var sequence = Seq(Container("a", SequenceStepType.Loop, new[] { middle }));

    GamepackWaitForImageMapping.Apply(sequence);

    deep.WaitForImage!.TimeoutMs.Should().Be(1234);
  }

  [Fact]
  public void AStepWithNoActionIsSkipped() {
    var empty = new SequenceStep { StepId = "n", StepType = SequenceStepType.Action };
    var sequence = Seq(empty);

    var act = () => GamepackWaitForImageMapping.Apply(sequence);

    act.Should().NotThrow();
    empty.WaitForImage.Should().BeNull();
  }

  [Fact]
  public void OtherStepTypesAreUnchanged() {
    var still = new SequenceStep { StepId = "w", StepType = SequenceStepType.Action, Action = new SequenceActionPayload { Type = "WaitForStill" } };
    var sequence = Seq(still);

    GamepackWaitForImageMapping.Apply(sequence);

    still.WaitForImage.Should().BeNull();
  }

  [Fact]
  public void AnExistingValueIsReplacedByThePayload() {
    var step = Wfi("a", 4000);
    step.WaitForImage = new WaitForImageConfig { TimeoutMs = 1 };

    GamepackWaitForImageMapping.Apply(Seq(step));

    step.WaitForImage!.TimeoutMs.Should().Be(4000);
  }

  [Fact]
  public void ANullSequenceThrows() {
    var act = () => GamepackWaitForImageMapping.Apply(null!);

    act.Should().Throw<ArgumentNullException>();
  }
}
