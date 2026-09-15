using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// Save-time validation of composite conditions (feature 088, issue #191): the size, depth and
/// child-shape rules, and the boundaries on either side of each limit. A limit that is only tested
/// from the rejecting side is half-tested — an off-by-one that rejects a legal guard would pass.
/// </summary>
public sealed class CompositeConditionValidationTests {
  private static List<string> Validate(SequenceStepCondition condition) {
    var errors = new List<string>();
    CompositeConditionValidator.Validate(condition, "S", errors);
    return errors;
  }

  private static ImageVisibleStepCondition Image(string id = "a") => new() { ImageId = id };

  /// <summary>Builds a chain of nested composites <paramref name="depth"/> condition levels deep.</summary>
  private static SequenceStepCondition Nest(int depth) {
    SequenceStepCondition current = Image();
    for (var level = 1; level < depth; level++) {
      current = new AllStepCondition { Children = new[] { current } };
    }

    return current;
  }

  // ---------- emptiness ----------

  [Fact]
  public void AnEmptyCompositeIsRejected() {
    var errors = Validate(new AllStepCondition());

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $: 'all' requires at least one child.");
  }

  [Theory]
  [InlineData("any")]
  [InlineData("none")]
  public void EveryRuleRejectsAnEmptyChildList(string rule) {
    CompositeStepCondition condition = rule == "any" ? new AnyStepCondition() : new NoneStepCondition();

    var errors = Validate(condition);

    errors.Should().ContainSingle()
      .Which.Should().Be($"Step 'S' condition at $: '{rule}' requires at least one child.");
  }

  [Fact]
  public void ASingleChildCompositeIsAccepted() {
    // Deliberately unlike the older flow-condition tree, which demands two children: a one-child
    // composite is a harmless identity, and rejecting it would force every tool that builds
    // conditions programmatically to special-case the single-element result.
    Validate(new AllStepCondition { Children = new[] { Image() } }).Should().BeEmpty();
  }

  // ---------- width ----------

  [Fact]
  public void ExactlySixteenChildrenAreAccepted() {
    var children = Enumerable.Range(0, 16).Select(i => Image($"img-{i}")).ToArray();

    Validate(new AllStepCondition { Children = children }).Should().BeEmpty();
  }

  [Fact]
  public void SeventeenChildrenAreRejectedAndTheLimitIsNamed() {
    var children = Enumerable.Range(0, 17).Select(i => Image($"img-{i}")).ToArray();

    var errors = Validate(new AllStepCondition { Children = children });

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $: 'all' allows at most 16 children (found 17).");
  }

  // ---------- depth ----------

  [Fact]
  public void ExactlyFourLevelsOfNestingAreAccepted() {
    Validate(Nest(4)).Should().BeEmpty();
  }

  [Fact]
  public void FiveLevelsOfNestingAreRejectedAndTheLimitIsNamed() {
    var errors = Validate(Nest(5));

    // Depth counts condition levels: the outermost composite is level 1, so the fifth level — four
    // "children" hops down — is the first one over the limit.
    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $.children[0].children[0].children[0].children[0]: condition nesting exceeds the maximum depth of 4.");
  }

  // ---------- child shape ----------

  [Fact]
  public void AChildImageConditionWithoutAnImageIdIsRejectedAtItsOwnPath() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { Image(), new ImageVisibleStepCondition { ImageId = "" } }
    };

    var errors = Validate(condition);

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $.children[1]: imageVisible condition requires imageId.");
  }

  [Theory]
  [InlineData(-0.1)]
  [InlineData(1.5)]
  public void AChildWithAnOutOfRangeSimilarityIsRejected(double similarity) {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "a", MinSimilarity = similarity } }
    };

    var errors = Validate(condition);

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $.children[0]: imageVisible minSimilarity must be within 0..1.");
  }

  [Fact]
  public void AChildCommandOutcomeWithoutAStepRefIsRejected() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { new CommandOutcomeStepCondition { StepRef = "", ExpectedState = "success" } }
    };

    var errors = Validate(condition);

    errors.Should().Contain("Step 'S' condition at $.children[0]: commandOutcome condition requires stepRef.");
  }

  [Fact]
  public void AChildCommandOutcomeWithAnUnknownExpectedStateIsRejected() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = "maybe" } }
    };

    var errors = Validate(condition);

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $.children[0]: commandOutcome expectedState must be one of success|failed|skipped|break|no_break.");
  }

  [Fact]
  public void EveryBadChildIsReportedNotJustTheFirst() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new ImageVisibleStepCondition { ImageId = "" },
        new CommandOutcomeStepCondition { StepRef = "", ExpectedState = "success" }
      }
    };

    var errors = Validate(condition);

    errors.Should().HaveCount(2);
  }

  [Fact]
  public void AProblemNestedTwoLevelsDownIsReportedAtItsFullPath() {
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        Image(),
        new NoneStepCondition { Children = new SequenceStepCondition[] { new ImageVisibleStepCondition { ImageId = "" } } }
      }
    };

    var errors = Validate(condition);

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $.children[1].children[0]: imageVisible condition requires imageId.");
  }

  // ---------- leaves at the root are left to the existing callers ----------

  [Fact]
  public void ALeafPassedDirectlyIsLeftToItsExistingCallerSoMessagesAreNotDuplicated() {
    Validate(new ImageVisibleStepCondition { ImageId = "" }).Should().BeEmpty();
  }

  [Fact]
  public void ALeafPassedDirectlyIsCheckedWhenTheCallerAsksForIt() {
    var errors = new List<string>();
    CompositeConditionValidator.Validate(
      new ImageVisibleStepCondition { ImageId = "" }, "S", errors, validateLeafAtRoot: true);

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $: imageVisible condition requires imageId.");
  }

  [Fact]
  public void ANullConditionProducesNoErrors() {
    Validate(null!).Should().BeEmpty();
  }
}
