using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA1707, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 193: the save checks of the parameter condition.</summary>
public sealed class ParameterConditionValidationTests {
  private static ParameterStepCondition Condition(string name, ParameterConditionValue? value = null, string op = "equals") =>
    new() { Name = name, Operator = op, Value = value };

  private static List<string> Shape(SequenceStepCondition condition) {
    var errors = new List<string>();
    CompositeConditionValidator.Validate(condition, "train", errors);
    return errors;
  }

  [Fact]
  public void ShapeMessagesForALeafAtTheRoot() {
    Shape(Condition("fighterBatch", new("0", true))).Should().BeEmpty();
    Shape(Condition("", new("0", true))).Should().Equal("Step 'train' condition at $: parameter condition requires name.");
    Shape(Condition("fighterBatch", new("0", true), "contains")).Should().Equal(
      "Step 'train' condition at $: parameter operator must be one of equals, notEquals, greaterThan, lessThan.");
    Shape(Condition("fighterBatch")).Should().Equal("Step 'train' condition at $: parameter condition requires value.");
  }

  [Fact]
  public void ShapeMessagesForANestedChild() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "camp" }, Condition("fighterBatch") }
    };
    Shape(condition).Should().Equal("Step 'train' condition at $.children[1]: parameter condition requires value.");
  }

  // ---------- Declarations ----------

  private static SequenceStep Step(string id, SequenceStepCondition? condition = null) => new() {
    StepId = id,
    CommandId = "cmd",
    Condition = condition,
    Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd" } }
  };

  private static CommandSequence Sequence(IEnumerable<SequenceStep> steps, params ParameterDeclaration[] declarations) {
    var sequence = new CommandSequence { Id = "s", Name = "s" };
    sequence.SetSteps(steps.ToList());
    foreach (var declaration in declarations) sequence.Parameters.Add(declaration);
    return sequence;
  }

  private static readonly ParameterDeclaration Batch = new() { Name = "fighterBatch", Type = ParameterValueType.Number };

  private static readonly ParameterDeclaration Tiers = new() {
    Name = "tiers",
    Type = ParameterValueType.Text,
    Kind = ParameterInputKind.Choice,
    Multiple = true,
    Options = new[] { new ParameterOption { Label = "T1", Value = "t1" }, new ParameterOption { Label = "Max", Value = "max" } }
  };

  private static IReadOnlyList<ParameterValidationIssue> Errors(CommandSequence sequence) =>
    ParameterValidationService.ValidateSequence(sequence, _ => null).Errors;

  [Theory]
  [InlineData("fighterBach")]
  [InlineData("{{fighterBatch}}")]
  [InlineData("queue.gameId")]
  public void AnUnknownNameIsRefused(string name) {
    var errors = Errors(Sequence(new[] { Step("train", Condition(name, new("0", true))) }, Batch));
    var issue = errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.UnknownConditionParameter).Subject;
    issue.Message.Should().Be($"Step 'train' condition at condition: parameter '{name}' is not a parameter of this sequence.");
    issue.FieldPath.Should().Be("condition");
    issue.ParameterName.Should().Be(name);
  }

  [Theory]
  [InlineData("abc", false)]
  [InlineData("2.5", true)]
  public void ANumberParameterNeedsAWholeNumber(string value, bool isNumber) {
    var errors = Errors(Sequence(new[] { Step("train", Condition("fighterBatch", new(value, isNumber))) }, Batch));
    errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.InvalidConditionValue)
      .Which.Message.Should().Be($"Step 'train' condition at condition: parameter 'fighterBatch' is a number parameter, and value '{value}' is not a whole number.");
  }

  [Theory]
  [InlineData("0", false)]
  [InlineData("0", true)]
  public void ANumberParameterAcceptsAWholeNumberAsTextOrNumber(string value, bool isNumber) {
    Errors(Sequence(new[] { Step("train", Condition("fighterBatch", new(value, isNumber))) }, Batch)).Should().BeEmpty();
  }

  [Fact]
  public void AMultiValueParameterIsRefused() {
    var errors = Errors(Sequence(new[] { Step("train", Condition("tiers", new("max", false))) }, Tiers));
    errors.Should().ContainSingle(e => e.Code == ParameterValidationCodes.ListInSingleField)
      .Which.Message.Should().Be("Step 'train' condition at condition: parameter 'tiers' holds a list, and a parameter condition needs one value.");
  }

  [Fact]
  public void EachSlotAndNestedBodyHasItsFieldPath() {
    var unknown = Condition("nope", new("0", true));
    var ifStep = new SequenceStep {
      StepId = "branch",
      StepType = SequenceStepType.If,
      If = new IfConfig {
        Condition = new AllStepCondition { Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "a" }, unknown } }
      },
      Body = new List<SequenceStep> { Step("inner", Condition("nope", new("0", true))) }
    };
    var loopStep = new SequenceStep {
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new WhileLoopConfig { Condition = Condition("nope", new("0", true)), MaxIterations = 3 },
      Body = new List<SequenceStep> {
        new() { StepId = "brk", StepType = SequenceStepType.Break, BreakCondition = Condition("nope", new("0", true)) }
      }
    };

    var paths = Errors(Sequence(new[] { ifStep, loopStep }, Batch))
      .Where(e => e.Code == ParameterValidationCodes.UnknownConditionParameter)
      .Select(e => e.Message.Split(':')[0])
      .ToList();

    paths.Should().BeEquivalentTo(
      "Step 'branch' condition at if.condition.children[1]",
      "Step 'inner' condition at condition",
      "Step 'loop' condition at loop.condition",
      "Step 'brk' condition at breakCondition");
  }

  [Fact]
  public void AnEmptyNameIsLeftToTheShapeCheck() {
    Errors(Sequence(new[] { Step("train", Condition("", new("0", true))) }, Batch)).Should().BeEmpty();
  }
}
