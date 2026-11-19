using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 193: the resolution and the evaluation of the parameter condition.</summary>
public sealed class ParameterConditionEvaluationTests {
  private static readonly Dictionary<string, string> NoOutcomes = new();

  private static ParameterDeclaration NumberDecl(string name, string? pattern = null) =>
    new() { Name = name, Type = ParameterValueType.Number, Pattern = pattern };

  private static ParameterDeclaration TextDecl(string name) => new() { Name = name, Type = ParameterValueType.Text };

  private static ParameterDeclaration ChoiceDecl(string name, ParameterValueType type, params string[] values) => new() {
    Name = name,
    Type = type,
    Kind = ParameterInputKind.Choice,
    Options = Array.ConvertAll(values, v => new ParameterOption { Label = "Label " + v, Value = v })
  };

  private static ParameterDeclaration ImageDecl(string name) =>
    new() { Name = name, Type = ParameterValueType.Text, Kind = ParameterInputKind.Image, AllowedImages = new[] { "camp-a", "camp-b" } };

  private static ParameterScope Scope(ParameterDeclaration declaration, string? value) =>
    ParameterScope.Empty
      .Child(ParameterScopeLayers.Entry, value is null ? null : new[] { new ParameterBinding { Name = declaration.Name, Value = value } }, null)
      .Child(ParameterScopeLayers.Sequence, null, new[] { declaration });

  private static ParameterStepCondition Condition(string name, string op, ParameterConditionValue value, bool negate = false) =>
    new() { Name = name, Operator = op, Value = value, Negate = negate };

  private static async Task<bool> EvaluateAsync(SequenceStepCondition condition, ParameterScope scope, string path = "condition") {
    SequenceStepConditionResolver.TryResolve(condition, scope, path, out var resolved, out var error, out _)
      .Should().BeTrue(error?.ToMessage("s"));
    var result = await SequenceStepConditionEvaluator.EvaluateAsync(resolved, null, NoOutcomes, CancellationToken.None);
    return result.Value;
  }

  // ---------- Number (US1) ----------

  [Theory]
  [InlineData("0", "equals", true)]
  [InlineData("250", "equals", false)]
  [InlineData("250", "notEquals", true)]
  [InlineData("250", "greaterThan", true)]
  [InlineData("250", "lessThan", false)]
  [InlineData("10", "greaterThan", true)]
  public async Task NumberParameterComparesAsAWholeNumber(string runValue, string op, bool expected) {
    var value = op == "greaterThan" && runValue == "10" ? "9" : "0";
    var result = await EvaluateAsync(Condition("fighterBatch", op, new(value, true)), Scope(NumberDecl("fighterBatch"), runValue));
    result.Should().Be(expected);
  }

  [Fact]
  public async Task NegateInvertsTheResult() {
    var scope = Scope(NumberDecl("fighterBatch"), "0");
    (await EvaluateAsync(Condition("fighterBatch", "equals", new("0", true), negate: true), scope)).Should().BeFalse();
    (await EvaluateAsync(Condition("fighterBatch", "equals", new("0", true), negate: true), Scope(NumberDecl("fighterBatch"), "250")))
      .Should().BeTrue();
  }

  [Fact]
  public async Task ATextValueThatIsAWholeNumberWorksForANumberParameter() {
    (await EvaluateAsync(Condition("fighterBatch", "equals", new("0", false)), Scope(NumberDecl("fighterBatch"), "0")))
      .Should().BeTrue();
  }

  [Fact]
  public async Task ANumberParameterWithAPatternComparesTheEnteredNumber() {
    var scope = Scope(NumberDecl("fighterBatch", "x{value}"), "5");
    (await EvaluateAsync(Condition("fighterBatch", "equals", new("5", true)), scope)).Should().BeTrue();
  }

  [Fact]
  public void NoValueGivesAnUnresolvedErrorWithTheFieldPath() {
    var ok = SequenceStepConditionResolver.TryResolve(
      Condition("fighterBatch", "equals", new("0", true)), Scope(NumberDecl("fighterBatch"), null), "if.condition",
      out _, out var error, out _);

    ok.Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
    error.ParameterName.Should().Be("fighterBatch");
    error.FieldPath.Should().Be("if.condition.name");
    SequenceRunner.ToFailureKind(error.Reason).Should().Be(ConditionEvaluationFailureKind.ParameterUnresolved);
  }

  [Fact]
  public void AnUndeclaredNameGivesAnUnresolvedError() {
    var scope = ParameterScope.Empty.Child(ParameterScopeLayers.Entry, new[] { new ParameterBinding { Name = "other", Value = "1" } }, null);
    SequenceStepConditionResolver.TryResolve(Condition("other", "equals", new("1", true)), scope, "condition", out _, out var error, out _)
      .Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.Unresolved);
  }

  [Fact]
  public void ARunValueThatIsNotAWholeNumberGivesNotANumber() {
    var ok = SequenceStepConditionResolver.TryResolve(
      Condition("fighterBatch", "equals", new("0", true)), Scope(NumberDecl("fighterBatch"), "abc"), "condition",
      out _, out var error, out _);

    ok.Should().BeFalse();
    error!.Reason.Should().Be(ParameterResolutionReasons.NotANumber);
    error.OffendingValue.Should().Be("abc");
    SequenceRunner.ToFailureKind("not_a_number").Should().Be(ConditionEvaluationFailureKind.ParameterValueInvalid);
    error.ToMessage("train").Should().Be(
      "Step 'train': parameter 'fighterBatch' resolved to 'abc', which is not a whole number for field 'condition.name'.");
  }

  [Fact]
  public void ResolveConditionThrowsWithTheFailureKind() {
    var unresolved = () => SequenceRunner.ResolveCondition(
      Condition("fighterBatch", "equals", new("0", true)), Scope(NumberDecl("fighterBatch"), null), "condition", "train");
    unresolved.Should().Throw<ConditionEvaluationException>().Which.Kind.Should().Be(ConditionEvaluationFailureKind.ParameterUnresolved);

    var invalid = () => SequenceRunner.ResolveCondition(
      Condition("fighterBatch", "equals", new("0", true)), Scope(NumberDecl("fighterBatch"), "abc"), "condition", "train");
    var thrown = invalid.Should().Throw<ConditionEvaluationException>().Which;
    thrown.Kind.Should().Be(ConditionEvaluationFailureKind.ParameterValueInvalid);
    thrown.Detail.Should().Be("fighterBatch");
  }

  [Fact]
  public async Task AnUnresolvedLeafAtTheEvaluatorThrows() {
    var evaluate = () => SequenceStepConditionEvaluator.EvaluateAsync(
      Condition("fighterBatch", "equals", new("0", true)), null, NoOutcomes, CancellationToken.None);
    var thrown = await evaluate.Should().ThrowAsync<ConditionEvaluationException>();
    thrown.Which.Kind.Should().Be(ConditionEvaluationFailureKind.ParameterUnresolved);
    thrown.Which.Detail.Should().Be("fighterBatch");
  }

  [Fact]
  public async Task EachEvaluationWritesOneLogLine() {
    using var _ = CommandStepStateScope.Push(new CommandStepStateScope());
    await EvaluateAsync(Condition("fighterBatch", "equals", new("0", true)), Scope(NumberDecl("fighterBatch"), "0"));
    CommandStepStateScope.DrainLog().Should().Be("parameter(name=fighterBatch, operator=equals, value=0) actual=0 compare=number");

    await EvaluateAsync(Condition("fighterTier", "equals", new("max", false)), Scope(TextDecl("fighterTier"), "MAX"));
    CommandStepStateScope.DrainLog().Should().Be("parameter(name=fighterTier, operator=equals, value=max) actual=MAX compare=text");
  }

  [Fact]
  public void DescribeShowsTheFieldsAndNegate() {
    SequenceStepConditionEvaluator.Describe(Condition("fighterBatch", "equals", new("0", true)))
      .Should().Be("parameter(name=fighterBatch, operator=equals, value=0)");
    SequenceStepConditionEvaluator.Describe(Condition("fighterBatch", "equals", new("0", true), negate: true))
      .Should().Be("NOT parameter(name=fighterBatch, operator=equals, value=0)");
  }

  [Fact]
  public void ResolutionReportsTheUsedValue() {
    SequenceStepConditionResolver.TryResolve(
      Condition("fighterBatch", "equals", new("0", true)), Scope(NumberDecl("fighterBatch"), "250"), "condition",
      out var resolved, out _, out var used).Should().BeTrue();
    used.Should().ContainSingle(u => u.Name == "fighterBatch" && u.Value == "250");
    var leaf = resolved.Should().BeOfType<ParameterStepCondition>().Subject;
    leaf.ResolvedValue.Should().Be("250");
    leaf.CompareAsNumber.Should().BeTrue();
  }

  // ---------- Text (US2) ----------

  [Theory]
  [InlineData("MAX", "equals", "max", true)]
  [InlineData("t7", "equals", "max", false)]
  [InlineData("t7", "notEquals", "max", true)]
  [InlineData("max ", "equals", "max", false)]
  [InlineData("a", "lessThan", "B", true)]
  [InlineData("B", "greaterThan", "a", true)]
  public async Task TextParameterComparesAsText(string runValue, string op, string value, bool expected) {
    (await EvaluateAsync(Condition("fighterTier", op, new(value, false)), Scope(TextDecl("fighterTier"), runValue)))
      .Should().Be(expected);
  }

  [Theory]
  [InlineData("MAX", "equals", true)]
  [InlineData("t7", "notEquals", true)]
  [InlineData("t7", "equals", false)]
  public async Task TextChoiceParameterComparesTheOptionValue(string runValue, string op, bool expected) {
    var declaration = ChoiceDecl("fighterTier", ParameterValueType.Text, "t1", "t7", "max");
    (await EvaluateAsync(Condition("fighterTier", op, new("max", false)), Scope(declaration, runValue))).Should().Be(expected);
    // The label is not the compared text.
    (await EvaluateAsync(Condition("fighterTier", "equals", new("Label max", false)), Scope(declaration, "max"))).Should().BeFalse();
  }

  [Fact]
  public async Task NumberChoiceParameterComparesAsANumber() {
    var declaration = ChoiceDecl("level", ParameterValueType.Number, "9", "10");
    (await EvaluateAsync(Condition("level", "greaterThan", new("9", true)), Scope(declaration, "10"))).Should().BeTrue();
  }

  [Fact]
  public async Task ImageParameterComparesTheImageIdAsText() {
    (await EvaluateAsync(Condition("camp", "equals", new("camp-a", false)), Scope(ImageDecl("camp"), "CAMP-A"))).Should().BeTrue();
  }

  [Fact]
  public async Task AJsonNumberValueComparesAsTextForATextParameter() {
    (await EvaluateAsync(Condition("fighterTier", "equals", new("0", true)), Scope(TextDecl("fighterTier"), "0"))).Should().BeTrue();
  }

  // ---------- Composites (US3) ----------

  private static ParameterStepCondition BatchIsZero() => Condition("fighterBatch", "equals", new("0", true));

  [Fact]
  public async Task AllWithAFalseParameterChildIsFalseAndDoesNotCallTheImageEvaluator() {
    var calls = 0;
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { BatchIsZero(), new ImageVisibleStepCondition { ImageId = "camp" } }
    };
    SequenceStepConditionResolver.TryResolve(condition, Scope(NumberDecl("fighterBatch"), "250"), "condition", out var resolved, out _, out _)
      .Should().BeTrue();

    var result = await SequenceStepConditionEvaluator.EvaluateAsync(
      resolved, (_, _) => { calls++; return Task.FromResult(true); }, NoOutcomes, CancellationToken.None);

    result.Value.Should().BeFalse();
    result.DecidingPath.Should().Be("$.children[0]");
    result.DecidingDescription.Should().Be("parameter(name=fighterBatch, operator=equals, value=0)");
    calls.Should().Be(0);
  }

  [Fact]
  public async Task AnyAndNoneFollowATrueParameterChild() {
    var scope = Scope(NumberDecl("fighterBatch"), "0");
    var any = new AnyStepCondition { Children = new SequenceStepCondition[] { BatchIsZero() } };
    var none = new NoneStepCondition { Children = new SequenceStepCondition[] { BatchIsZero() } };

    (await EvaluateAsync(any, scope)).Should().BeTrue();
    (await EvaluateAsync(none, scope)).Should().BeFalse();
  }

  [Fact]
  public void ANestedChildReportsItsFieldPath() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new AnyStepCondition {
          Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "camp" }, BatchIsZero() }
        }
      }
    };

    SequenceStepConditionResolver.TryResolve(condition, Scope(NumberDecl("fighterBatch"), null), "if.condition", out _, out var error, out _)
      .Should().BeFalse();
    error!.FieldPath.Should().Be("if.condition.children[0].children[1].name");
  }

  [Fact]
  public void AConditionWithNoParameterLeafStaysTheSameInstance() {
    var condition = new AllStepCondition { Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "camp" } } };
    SequenceStepConditionResolver.TryResolve(condition, ParameterScope.Empty, "condition", out var resolved, out _, out _).Should().BeTrue();
    resolved.Should().BeSameAs(condition);
  }

  [Fact]
  public void BreakConditionDescriptionNamesTheParameter() {
    SequenceRunner.DescribeBreakCondition(BatchIsZero())
      .Should().Be(("parameter", "parameter(name=fighterBatch, operator=equals, value=0)"));
  }
}
