using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Truth tables, short-circuit behaviour and negation for composite conditions (feature 088,
/// issue #191). These run against the evaluator directly — no sequence, session or emulator — so a
/// combining rule is pinned by its own test rather than inferred from an end-to-end run.
/// </summary>
public sealed class CompositeConditionEvaluatorTests {
  /// <summary>
  /// Answers image leaves from a lookup and counts how many were asked, which is how the
  /// short-circuit assertions tell "stopped early" from "evaluated everything and got the same answer".
  /// </summary>
  private sealed class CountingImageEvaluator {
    private readonly Dictionary<string, bool> _visible;

    public CountingImageEvaluator(Dictionary<string, bool> visible) {
      _visible = visible;
    }

    public List<string> Asked { get; } = new();

    public Task<bool> EvaluateAsync(GameBot.Domain.Commands.Blocks.Condition condition, CancellationToken ct) {
      _ = ct;
      Asked.Add(condition.TargetId);
      return Task.FromResult(_visible.TryGetValue(condition.TargetId, out var value) && value);
    }
  }

  private static ImageVisibleStepCondition Image(string id, bool negate = false) =>
    new() { ImageId = id, Negate = negate };

  private static Task<ConditionEvaluation> EvaluateAsync(
      SequenceStepCondition condition,
      CountingImageEvaluator evaluator,
      IReadOnlyDictionary<string, string>? outcomes = null) =>
    SequenceStepConditionEvaluator.EvaluateAsync(
      condition,
      evaluator.EvaluateAsync,
      outcomes ?? new Dictionary<string, string>(),
      CancellationToken.None);

  // ---------- all ----------

  [Theory]
  [InlineData(true, true, true)]
  [InlineData(true, false, false)]
  [InlineData(false, true, false)]
  [InlineData(false, false, false)]
  public async Task AllIsTrueOnlyWhenEveryChildIsTrue(bool first, bool second, bool expected) {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = first, ["b"] = second });
    var condition = new AllStepCondition { Children = new[] { Image("a"), Image("b") } };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().Be(expected);
  }

  [Fact]
  public async Task AllStopsAtTheFirstFalseChild() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = false, ["b"] = true });
    var condition = new AllStepCondition { Children = new[] { Image("a"), Image("b") } };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().BeFalse();
    evaluator.Asked.Should().Equal("a");
    result.DecidingPath.Should().Be("$.children[0]");
    result.DecidingDescription.Should().Contain("imageId=a");
  }

  // ---------- any ----------

  [Theory]
  [InlineData(true, true, true)]
  [InlineData(true, false, true)]
  [InlineData(false, true, true)]
  [InlineData(false, false, false)]
  public async Task AnyIsTrueWhenAtLeastOneChildIsTrue(bool first, bool second, bool expected) {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = first, ["b"] = second });
    var condition = new AnyStepCondition { Children = new[] { Image("a"), Image("b") } };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().Be(expected);
  }

  [Fact]
  public async Task AnyStopsAtTheFirstTrueChild() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true, ["b"] = true });
    var condition = new AnyStepCondition { Children = new[] { Image("a"), Image("b") } };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().BeTrue();
    evaluator.Asked.Should().Equal("a");
  }

  // ---------- none ----------

  [Theory]
  [InlineData(true, true, false)]
  [InlineData(true, false, false)]
  [InlineData(false, true, false)]
  [InlineData(false, false, true)]
  public async Task NoneIsTrueOnlyWhenNoChildIsTrue(bool first, bool second, bool expected) {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = first, ["b"] = second });
    var condition = new NoneStepCondition { Children = new[] { Image("a"), Image("b") } };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().Be(expected);
  }

  [Fact]
  public async Task NoneStopsAtTheFirstTrueChild() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true, ["b"] = false });
    var condition = new NoneStepCondition { Children = new[] { Image("a"), Image("b") } };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().BeFalse();
    evaluator.Asked.Should().Equal("a");
  }

  // ---------- ordering, negation, nesting ----------

  [Fact]
  public async Task ChildrenAreEvaluatedInAuthorOrder() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true, ["b"] = true, ["c"] = true });
    var condition = new AllStepCondition { Children = new[] { Image("a"), Image("b"), Image("c") } };

    await EvaluateAsync(condition, evaluator);

    evaluator.Asked.Should().Equal("a", "b", "c");
  }

  [Theory]
  [InlineData(true, true)]
  [InlineData(false, false)]
  public async Task NegateInvertsTheCombinedResult(bool childVisible, bool combinedBeforeNegation) {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = childVisible });
    var condition = new AllStepCondition { Children = new[] { Image("a") }, Negate = true };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().Be(!combinedBeforeNegation);
  }

  [Theory]
  [InlineData(true, true)]
  [InlineData(true, false)]
  [InlineData(false, true)]
  [InlineData(false, false)]
  public async Task NegatedAnyAgreesWithNoneOverTheSameChildren(bool first, bool second) {
    var visible = new Dictionary<string, bool> { ["a"] = first, ["b"] = second };

    var negatedAny = await EvaluateAsync(
      new AnyStepCondition { Children = new[] { Image("a"), Image("b") }, Negate = true },
      new CountingImageEvaluator(visible));

    var none = await EvaluateAsync(
      new NoneStepCondition { Children = new[] { Image("a"), Image("b") } },
      new CountingImageEvaluator(visible));

    negatedAny.Value.Should().Be(none.Value);
  }

  [Fact]
  public async Task NegateOnALeafChildIsAppliedBeforeTheRuleCombinesIt() {
    // The B-011 guard written with negate rather than a nested none: the button is up, the
    // look-alike dialog's title is also up, so the guard must be false.
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["confirm"] = true, ["gas-title"] = true });
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { Image("confirm"), Image("gas-title", negate: true) }
    };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().BeFalse();
    result.DecidingPath.Should().Be("$.children[1]");
  }

  [Fact]
  public async Task NestedCompositesEvaluateAndReportADeepDecidingPath() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["confirm"] = true, ["gas-title"] = true });
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        Image("confirm"),
        new NoneStepCondition { Children = new[] { Image("gas-title") } }
      }
    };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().BeFalse();
    result.DecidingPath.Should().Be("$.children[1]");
  }

  [Fact]
  public async Task SingleChildCompositeEvaluatesAsThatChild() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true });

    var result = await EvaluateAsync(new AllStepCondition { Children = new[] { Image("a") } }, evaluator);

    result.Value.Should().BeTrue();
  }

  [Fact]
  public async Task WhenNoChildSettlesTheResultNoDecidingChildIsReported() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true, ["b"] = true });

    var result = await EvaluateAsync(new AllStepCondition { Children = new[] { Image("a"), Image("b") } }, evaluator);

    result.Value.Should().BeTrue();
    result.DecidingPath.Should().BeNull();
  }

  // ---------- mixed kinds and failure propagation ----------

  [Fact]
  public async Task CompositeCombinesAnImageLeafWithACommandOutcomeLeaf() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true });
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = "success" },
        Image("a")
      }
    };

    var result = await EvaluateAsync(condition, evaluator, new Dictionary<string, string> { ["probe"] = "success" });

    result.Value.Should().BeTrue();
  }

  // Feature 103 (issue #193, FR-004): the run-time half of the reported ceiling. A reference to a
  // step nested inside a Loop/If body must resolve from inside every composite rule, and must tell
  // a Break that fired from one that did not. Nesting depth is irrelevant to the lookup by
  // construction — outcomes are keyed by step id — but that is exactly the kind of "obviously fine"
  // claim issue #193 exists to stop us asserting without measuring.

  [Theory]
  [InlineData("all", "break", true)]
  [InlineData("any", "break", true)]
  [InlineData("none", "break", false)]
  [InlineData("all", "no_break", false)]
  [InlineData("any", "no_break", false)]
  [InlineData("none", "no_break", true)]
  public async Task ANestedBreakReferenceResolvesInsideEveryCompositeRule(
      string rule, string expectedState, bool expected) {
    // "nested-break" sat inside a loop body and fired, so its recorded outcome is "break".
    var outcomes = new Dictionary<string, string> { ["nested-break"] = "break" };
    var child = new CommandOutcomeStepCondition { StepRef = "nested-break", ExpectedState = expectedState };

    CompositeStepCondition condition = rule switch {
      "all" => new AllStepCondition { Children = new[] { child } },
      "any" => new AnyStepCondition { Children = new[] { child } },
      _ => new NoneStepCondition { Children = new[] { child } }
    };

    var result = await EvaluateAsync(condition, new CountingImageEvaluator(new Dictionary<string, bool>()), outcomes);

    result.Value.Should().Be(expected);
  }

  [Fact]
  public async Task ANestedBreakReferenceResolvesThroughACompositeWithinAComposite() {
    var outcomes = new Dictionary<string, string> { ["nested-break"] = "no_break" };
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = true });
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        Image("a"),
        new AnyStepCondition {
          Children = new SequenceStepCondition[] {
            new CommandOutcomeStepCondition { StepRef = "nested-break", ExpectedState = "no_break" }
          }
        }
      }
    };

    var result = await EvaluateAsync(condition, evaluator, outcomes);

    result.Value.Should().BeTrue();
  }

  [Fact]
  public async Task AChildThatCannotBeEvaluatedFailsRatherThanCountingAsFalse() {
    // A guard that could not be answered must not look like "the screen did not match" — that turns
    // a broken setup into a silently skipped step.
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool>());
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new CommandOutcomeStepCondition { StepRef = "never-ran", ExpectedState = "success" }
      }
    };

    Func<Task> act = () => EvaluateAsync(condition, evaluator);

    var failure = await act.Should().ThrowAsync<ConditionEvaluationException>().ConfigureAwait(false);
    failure.Which.Kind.Should().Be(ConditionEvaluationFailureKind.CommandOutcomeUnavailable);
    failure.Which.Detail.Should().Be("never-ran");
  }

  [Fact]
  public async Task ShortCircuitingMeansALaterUnevaluableChildIsNeverReached() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = false });
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        Image("a"),
        new CommandOutcomeStepCondition { StepRef = "never-ran", ExpectedState = "success" }
      }
    };

    var result = await EvaluateAsync(condition, evaluator);

    result.Value.Should().BeFalse();
  }

  [Fact]
  public async Task AMissingImageEvaluatorFailsRatherThanAnsweringFalse() {
    var condition = new AllStepCondition { Children = new[] { Image("a") } };

    Func<Task> act = () => SequenceStepConditionEvaluator.EvaluateAsync(
      condition, imageEvaluator: null, new Dictionary<string, string>(), CancellationToken.None);

    var failure = await act.Should().ThrowAsync<ConditionEvaluationException>().ConfigureAwait(false);
    failure.Which.Kind.Should().Be(ConditionEvaluationFailureKind.ImageEvaluatorUnavailable);
  }

  [Fact]
  public async Task AnEmptyCompositeFailsLoudlyInsteadOfInventingATruthValue() {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool>());

    Func<Task> act = () => EvaluateAsync(new AllStepCondition(), evaluator);

    await act.Should().ThrowAsync<ConditionEvaluationException>().ConfigureAwait(false);
  }

  // ---------- leaf behaviour is unchanged ----------

  [Theory]
  [InlineData(true, false, true)]
  [InlineData(true, true, false)]
  [InlineData(false, false, false)]
  [InlineData(false, true, true)]
  public async Task LeafConditionsStillEvaluateExactlyAsBefore(bool visible, bool negate, bool expected) {
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["a"] = visible });

    var result = await EvaluateAsync(Image("a", negate), evaluator);

    result.Value.Should().Be(expected);
    result.DecidingPath.Should().BeNull();
  }

  // ---------- cost ----------

  [Fact]
  public async Task ATwoImageGuardAsksForNoMoreLeafEvaluationsThanItNeeds() {
    // SC-006 / FR-017: adding a second signal costs at most one extra leaf evaluation, and the
    // evaluator performs no capture of its own between children.
    var evaluator = new CountingImageEvaluator(new Dictionary<string, bool> { ["confirm"] = true, ["title"] = true });
    var condition = new AllStepCondition { Children = new[] { Image("confirm"), Image("title") } };

    await EvaluateAsync(condition, evaluator);

    evaluator.Asked.Should().HaveCount(2);
  }

  [Fact]
  public void DescribeRendersACompositeAsItsRuleOverItsChildren() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { Image("confirm"), Image("gas-title", negate: true) }
    };

    var description = SequenceStepConditionEvaluator.Describe(condition);

    description.Should().Be("all(imageVisible(imageId=confirm, minSimilarity=default), NOT imageVisible(imageId=gas-title, minSimilarity=default))");
  }
}
