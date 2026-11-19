using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 193: the operators, the shape rules, the compare rules, and the JSON form of the parameter condition.</summary>
public sealed class ParameterConditionRulesTests {
  private static ParameterConditionValue Number(string text) => new(text, true);

  private static ParameterConditionValue Text(string text) => new(text, false);

  [Theory]
  [InlineData("0", "0", "equals", true)]
  [InlineData("250", "0", "equals", false)]
  [InlineData("250", "0", "notEquals", true)]
  [InlineData("250", "0", "greaterThan", true)]
  [InlineData("250", "0", "lessThan", false)]
  [InlineData("10", "9", "greaterThan", true)]
  [InlineData("10", "9", "lessThan", false)]
  [InlineData("-5", "0", "lessThan", true)]
  [InlineData("-5", "0", "greaterThan", false)]
  [InlineData(" 12 ", "12", "equals", true)]
  public void NumberCompareUsesWholeNumbers(string runValue, string value, string op, bool expected) {
    ParameterConditionRules.Compare(runValue, compareAsNumber: true, Number(value), op).Should().Be(expected);
  }

  [Theory]
  [InlineData("MAX", "max", "equals", true)]
  [InlineData("t7", "max", "equals", false)]
  [InlineData("t7", "max", "notEquals", true)]
  [InlineData("max ", "max", "equals", false)]
  [InlineData("a", "B", "lessThan", true)]
  [InlineData("B", "a", "greaterThan", true)]
  [InlineData("10", "9", "greaterThan", false)]
  public void TextCompareIsOrdinalAndCaseInsensitive(string runValue, string value, string op, bool expected) {
    ParameterConditionRules.Compare(runValue, compareAsNumber: false, Text(value), op).Should().Be(expected);
  }

  [Fact]
  public void AJsonNumberValueComparesAsItsTextForATextParameter() {
    ParameterConditionRules.Compare("0", compareAsNumber: false, Number("0"), "equals").Should().BeTrue();
  }

  [Fact]
  public void AnUnknownOperatorOrABadNumberThrows() {
    var unknown = () => ParameterConditionRules.Compare("0", false, Text("0"), "contains");
    unknown.Should().Throw<ArgumentException>();
    var notANumber = () => ParameterConditionRules.Compare("abc", true, Number("0"), "equals");
    notANumber.Should().Throw<FormatException>();
  }

  [Fact]
  public void ShapeGivesEachMessage() {
    ParameterConditionRules.ValidateShape(new ParameterStepCondition { Name = "a", Operator = "equals", Value = Number("0") })
      .Should().BeEmpty();
    ParameterConditionRules.ValidateShape(new ParameterStepCondition { Name = " ", Operator = "Equals" })
      .Should().Equal(
        "parameter condition requires name.",
        "parameter operator must be one of equals, notEquals, greaterThan, lessThan.",
        "parameter condition requires value.");
  }

  [Theory]
  [InlineData(" 12 ", true, 12)]
  [InlineData("-3", true, -3)]
  [InlineData("2.5", false, 0)]
  [InlineData("abc", false, 0)]
  [InlineData("", false, 0)]
  [InlineData(null, false, 0)]
  public void TryParseWholeAcceptsOnlyWholeNumbers(string? text, bool ok, long expected) {
    ParameterConditionRules.TryParseWhole(text, out var number).Should().Be(ok);
    if (ok) number.Should().Be(expected);
  }

  [Fact]
  public void TheConverterKeepsTheJsonKind() {
    SequenceStepCondition number = new ParameterStepCondition { Name = "fighterBatch", Operator = "equals", Value = Number("0") };
    var numberJson = JsonSerializer.Serialize(number);
    numberJson.Should().Contain("\"type\":\"parameter\"").And.Contain("\"Value\":0");
    var numberBack = JsonSerializer.Deserialize<SequenceStepCondition>(numberJson);
    numberBack.Should().BeOfType<ParameterStepCondition>().Which.Value.Should().Be(Number("0"));

    SequenceStepCondition text = new ParameterStepCondition { Name = "fighterTier", Operator = "equals", Value = Text("0"), Negate = true };
    var textJson = JsonSerializer.Serialize(text);
    textJson.Should().Contain("\"Value\":\"0\"");
    var textBack = JsonSerializer.Deserialize<SequenceStepCondition>(textJson);
    textBack.Should().BeOfType<ParameterStepCondition>().Which.Should().BeEquivalentTo(text);
  }

  [Fact]
  public void TheRunTimeFieldsAreNotStored() {
    var json = JsonSerializer.Serialize<SequenceStepCondition>(new ParameterStepCondition {
      Name = "a", Operator = "equals", Value = Number("1"), ResolvedValue = "1", CompareAsNumber = true
    });
    json.Should().NotContain("ResolvedValue").And.NotContain("CompareAsNumber");
  }

  [Theory]
  [InlineData("true")]
  [InlineData("{}")]
  [InlineData("[]")]
  public void TheConverterRefusesOtherKinds(string value) {
    var json = "{\"type\":\"parameter\",\"Name\":\"a\",\"Operator\":\"equals\",\"Value\":" + value + "}";
    var read = () => JsonSerializer.Deserialize<SequenceStepCondition>(json);
    read.Should().Throw<JsonException>();
  }

  [Fact]
  public async Task TheFileRepositoryRoundTripsTheCondition() {
    var root = Path.Combine(Path.GetTempPath(), "gb-param-cond-" + Guid.NewGuid().ToString("N"));
    try {
      var repo = new FileSequenceRepository(root);
      var sequence = new CommandSequence { Id = "seq-param", Name = "seq" };
      sequence.SetSteps(new List<SequenceStep> {
        new() {
          Order = 0,
          StepId = "train",
          CommandId = "cmd",
          Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd" } },
          Condition = new AllStepCondition {
            Children = new SequenceStepCondition[] {
              new ParameterStepCondition { Name = "fighterBatch", Operator = "greaterThan", Value = Number("0") },
              new ParameterStepCondition { Name = "fighterTier", Operator = "notEquals", Value = Text("max"), Negate = true }
            }
          }
        }
      });

      await repo.CreateAsync(sequence);
      var back = await repo.GetAsync("seq-param");

      var children = back!.Steps[0].Condition.Should().BeOfType<AllStepCondition>().Subject.Children;
      children[0].Should().BeOfType<ParameterStepCondition>().Which.Value.Should().Be(Number("0"));
      var second = children[1].Should().BeOfType<ParameterStepCondition>().Subject;
      second.Value.Should().Be(Text("max"));
      second.Negate.Should().BeTrue();
      second.Operator.Should().Be("notEquals");
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }

  [Fact]
  public async Task TheFileRepositoryRefusesABadShape() {
    var root = Path.Combine(Path.GetTempPath(), "gb-param-cond-" + Guid.NewGuid().ToString("N"));
    try {
      var repo = new FileSequenceRepository(root);
      var sequence = new CommandSequence { Id = "seq-bad", Name = "seq" };
      sequence.SetSteps(new List<SequenceStep> {
        new() {
          Order = 0,
          StepId = "train",
          CommandId = "cmd",
          Action = new SequenceActionPayload { Type = "command", Parameters = { ["commandId"] = "cmd" } },
          Condition = new ParameterStepCondition { Name = "fighterBatch", Operator = "equals" }
        }
      });

      var create = () => repo.CreateAsync(sequence);
      await create.Should().ThrowAsync<InvalidOperationException>().WithMessage("Step 'train' parameter condition requires value.");
    }
    finally {
      if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
  }
}
