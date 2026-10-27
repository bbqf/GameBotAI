using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Feature 165: the map from a parameter resolution reason to a condition failure kind.
/// </summary>
public sealed class SequenceRunnerConditionFailureKindTests {
  [Fact]
  public void InvalidPatternMapsToParameterValueInvalid() {
    SequenceRunner.ToFailureKind(ParameterResolutionReasons.InvalidPattern)
        .Should().Be(ConditionEvaluationFailureKind.ParameterValueInvalid);
  }

  [Fact]
  public void UnresolvedMapsToParameterUnresolved() {
    SequenceRunner.ToFailureKind(ParameterResolutionReasons.Unresolved)
        .Should().Be(ConditionEvaluationFailureKind.ParameterUnresolved);
  }

  [Fact]
  public void UnknownNameInImageIdThrowsWithParameterUnresolved() {
    var condition = new ImageVisibleStepCondition { ImageId = "{{novaOption}}" };

    var act = () => SequenceRunner.ResolveCondition(condition, ParameterScope.Empty, "condition", "s1");

    act.Should().Throw<ConditionEvaluationException>()
        .Which.Kind.Should().Be(ConditionEvaluationFailureKind.ParameterUnresolved);
  }

  [Fact]
  public void InvalidResolvedPatternThrowsWithParameterValueInvalid() {
    var condition = new OcrTextStepCondition { Region = new PixelRegion(1, 1, 10, 10), Pattern = "{{countPattern}}" };
    var scope = ParameterScope.Empty.Child(
        ParameterScopeLayers.Entry,
        new[] { new ParameterBinding { Name = "countPattern", Value = "(" } },
        null);

    var act = () => SequenceRunner.ResolveCondition(condition, scope, "condition", "s1");

    var ex = act.Should().Throw<ConditionEvaluationException>().Which;
    ex.Kind.Should().Be(ConditionEvaluationFailureKind.ParameterValueInvalid);
    ex.Detail.Should().Be("countPattern");
    ex.Message.Should().Contain("not a valid regular expression");
  }
}
