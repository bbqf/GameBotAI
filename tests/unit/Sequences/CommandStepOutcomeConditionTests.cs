using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Sequences;

/// <summary>Feature 167: the evaluator reads the state of one step inside a command.</summary>
public sealed class CommandStepOutcomeConditionTests {
  private static CommandStepOutcomeStepCondition Condition(string expected, int order = 12, bool negate = false) => new() {
    StepRef = "send-1",
    CommandStepOrder = order,
    ExpectedState = expected,
    Negate = negate
  };

  private static IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> States(int order, string state) =>
    new Dictionary<string, IReadOnlyDictionary<int, string>>(System.StringComparer.OrdinalIgnoreCase) {
      ["send-1"] = new Dictionary<int, string> { [order] = state }
    };

  private static async Task<bool> EvaluateAsync(SequenceStepCondition condition, IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>>? states) {
    var result = await SequenceStepConditionEvaluator.EvaluateAsync(
      condition, null, new Dictionary<string, string>(), null, states, default);
    return result.Value;
  }

  [Fact]
  public async Task TrueWhenTheStateEqualsTheExpectedState() {
    (await EvaluateAsync(Condition("executed"), States(12, "executed"))).Should().BeTrue();
  }

  [Fact]
  public async Task FalseWhenTheStateIsAnotherState() {
    (await EvaluateAsync(Condition("executed"), States(12, "skipped_detection_failed"))).Should().BeFalse();
  }

  [Fact]
  public async Task NegateInvertsTheResult() {
    (await EvaluateAsync(Condition("executed", negate: true), States(12, "executed"))).Should().BeFalse();
    (await EvaluateAsync(Condition("executed", negate: true), States(12, "completed_timeout"))).Should().BeTrue();
  }

  [Fact]
  public async Task TheMatchIsNotCaseSensitive() {
    (await EvaluateAsync(Condition("EXECUTED"), States(12, "executed"))).Should().BeTrue();
  }

  [Fact]
  public async Task AnotherOrderDoesNotMatch() {
    (await EvaluateAsync(Condition("executed", order: 3), States(12, "executed"))).Should().BeFalse();
  }

  [Fact]
  public async Task AMissingOutcomeIsFalseAndNegateMakesItTrue() {
    (await EvaluateAsync(Condition("executed"), new Dictionary<string, IReadOnlyDictionary<int, string>>())).Should().BeFalse();
    (await EvaluateAsync(Condition("executed", negate: true), new Dictionary<string, IReadOnlyDictionary<int, string>>())).Should().BeTrue();
  }

  [Fact]
  public async Task TheAmbientScopeAnswersWhenNoMapIsGiven() {
    var scope = new CommandStepStateScope();
    scope.Set("send-1", new Dictionary<int, string> { [12] = "image_detected" });
    using var _ = CommandStepStateScope.Push(scope);

    (await EvaluateAsync(Condition("image_detected"), null)).Should().BeTrue();
    (await EvaluateAsync(Condition("executed"), null)).Should().BeFalse();
  }

  [Fact]
  public async Task AllRulesWorkInsideAComposite() {
    var all = new AllStepCondition { Children = new SequenceStepCondition[] { Condition("executed"), Condition("image_detected", order: 13) } };
    var states = new Dictionary<string, IReadOnlyDictionary<int, string>> {
      ["send-1"] = new Dictionary<int, string> { [12] = "executed", [13] = "image_detected" }
    };

    (await EvaluateAsync(all, states)).Should().BeTrue();
  }

  [Fact]
  public void TheConditionRoundTripsInJson() {
    SequenceStepCondition condition = Condition("executed");

    var json = JsonSerializer.Serialize(condition);
    json.Should().Contain("\"type\":\"commandStepOutcome\"");
    var back = JsonSerializer.Deserialize<SequenceStepCondition>(json);

    back.Should().BeOfType<CommandStepOutcomeStepCondition>()
      .Which.Should().BeEquivalentTo(condition);
  }

  [Fact]
  public void DescribeShowsTheFields() {
    SequenceStepConditionEvaluator.Describe(Condition("executed", negate: true))
      .Should().Be("NOT commandStepOutcome(stepRef=send-1, order=12, expected=executed)");
  }
}
