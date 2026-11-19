using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Parameters;
using Xunit;

#pragma warning disable CA1707, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 193 (FR-016): a gamepack export and import keep the parameter condition with all its fields.</summary>
public sealed class ParameterConditionGamepackTests {
  [Fact]
  public void TheConditionRoundTripsThroughTheGamepackFile() {
    var sequence = new CommandSequence { Id = "seq-train", Name = "Train" };
    sequence.Parameters.Add(new ParameterDeclaration { Name = "fighterBatch", Type = ParameterValueType.Number });
    sequence.Parameters.Add(new ParameterDeclaration { Name = "fighterTier" });
    sequence.SetSteps(new List<SequenceStep> {
      new() {
        Order = 0,
        StepId = "train",
        CommandId = "cmd",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd" } },
        Condition = new ParameterStepCondition {
          Name = "fighterBatch", Operator = "equals", Value = new ParameterConditionValue("0", true), Negate = true
        }
      },
      new() {
        Order = 1,
        StepId = "tier",
        CommandId = "cmd",
        Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd" } },
        Condition = new AllStepCondition {
          Children = new SequenceStepCondition[] {
            new ParameterStepCondition { Name = "fighterTier", Operator = "notEquals", Value = new ParameterConditionValue("max", false) }
          }
        }
      }
    });

    var empty = GamepackFileSerializer.Parse(GamepackTestKit.Pack("p", "1")).File!;
    var file = new GamepackFile { Manifest = empty.Manifest, Sequences = new[] { sequence } };

    var text = GamepackFileSerializer.Serialize(file);
    var parsed = GamepackFileSerializer.Parse(text);

    parsed.Problems.Should().BeEmpty();
    var steps = parsed.File!.Sequences.Single().Steps;
    var root = steps[0].Condition.Should().BeOfType<ParameterStepCondition>().Subject;
    root.Name.Should().Be("fighterBatch");
    root.Operator.Should().Be("equals");
    root.Value.Should().Be(new ParameterConditionValue("0", true));
    root.Negate.Should().BeTrue();

    var child = steps[1].Condition.Should().BeOfType<AllStepCondition>().Subject.Children.Single()
      .Should().BeOfType<ParameterStepCondition>().Subject;
    child.Name.Should().Be("fighterTier");
    child.Operator.Should().Be("notEquals");
    child.Value.Should().Be(new ParameterConditionValue("max", false));
    child.Negate.Should().BeFalse();
  }
}
