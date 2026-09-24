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

  // Feature 103 (issue #193, FR-004/FR-013): the reported ceiling's second half was that
  // expectedState accepted only success|failed|skipped, with no way to ask whether a nested Break
  // had fired. The set is pinned from both sides here — all five accepted, and the set closed
  // against additions — because "accepts break now" was exactly the claim nobody re-measured.

  [Theory]
  [InlineData("success")]
  [InlineData("failed")]
  [InlineData("skipped")]
  [InlineData("break")]
  [InlineData("no_break")]
  public void EveryAcceptedExpectedStateIsAcceptedInsideEveryCompositeRule(string expectedState) {
    foreach (var rule in new[] { "all", "any", "none" }) {
      var child = new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = expectedState };
      CompositeStepCondition condition = rule switch {
        "all" => new AllStepCondition { Children = new[] { child } },
        "any" => new AnyStepCondition { Children = new[] { child } },
        _ => new NoneStepCondition { Children = new[] { child } }
      };

      Validate(condition).Should().BeEmpty($"'{expectedState}' is accepted inside '{rule}'");
    }
  }

  [Theory]
  [InlineData("succeeded")]
  [InlineData("broke")]
  [InlineData("nobreak")]
  [InlineData("SUCCESS ")]
  public void AnExpectedStateOutsideTheFiveIsRejectedSoTheSetStaysClosed(string expectedState) {
    // FR-013 forbids adding outcome states. Near-misses are the cases that would reveal an
    // accidental widening — a trailing space or a plausible synonym slipping through.
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] { new CommandOutcomeStepCondition { StepRef = "probe", ExpectedState = expectedState } }
    };

    Validate(condition).Should().ContainSingle()
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

  // ---------- callers without a position index keep shape-only validation ----------

  [Fact]
  public void ACallerThatSuppliesNoPositionIndexDoesNotGetReferenceCheckedAndSoCannotNewlyReject() {
    // Feature 103 made the reference rules available to this validator, but only to callers that can
    // supply the authored-order index. The repository's own composite walk cannot: it validates a
    // condition in isolation, with no sequence around it. A rejection from there surfaces as a 500,
    // which is the failure mode this validator exists to prevent — so an unsupplied index must mean
    // "check the shape and nothing else", not "treat every reference as unresolvable".
    var condition = new AllStepCondition {
      Children = new SequenceStepCondition[] {
        new CommandOutcomeStepCondition { StepRef = "no-such-step", ExpectedState = "success" }
      }
    };

    Validate(condition).Should().BeEmpty();
  }

  [Fact]
  public void AnIndexWithoutAReferencingPositionResolvesButDoesNotOrder() {
    // Resolution and ordering are separate rules, and "prior" is meaningless without knowing where
    // the asking condition sits. Given the index alone, an absent reference is still caught while an
    // out-of-order one is not.
    var index = new Dictionary<string, int> { ["earlier"] = 0, ["later"] = 2 };

    var resolvable = new List<string>();
    CompositeConditionValidator.Validate(
      new AllStepCondition { Children = new SequenceStepCondition[] { new CommandOutcomeStepCondition { StepRef = "later", ExpectedState = "success" } } },
      "S", resolvable, positionByStepId: index);
    resolvable.Should().BeEmpty("ordering cannot be judged without the referencing step's position");

    var unresolvable = new List<string>();
    CompositeConditionValidator.Validate(
      new AllStepCondition { Children = new SequenceStepCondition[] { new CommandOutcomeStepCondition { StepRef = "absent", ExpectedState = "success" } } },
      "S", unresolvable, positionByStepId: index);
    unresolvable.Should().ContainSingle()
      .Which.Should().Be("Step 'S' condition at $.children[0]: commandOutcome references unknown prior step 'absent'.");
  }

  // ---------- lastRun (feature 105) ----------

  public static TheoryData<LastRunStepCondition, string> BadLastRunConditions => new() {
    { new LastRunStepCondition { Sequence = "", Status = "success", Since = "11:00" }, "lastRun condition requires sequence ('self' or a sequence id)." },
    { new LastRunStepCondition { Sequence = "self", Status = "failed", Since = "11:00" }, "lastRun status must be one of success|failure|cancelled." },
    { new LastRunStepCondition { Sequence = "self", Status = "success", Since = "11:00", Within = "01:00:00" }, "lastRun condition accepts only one of since or within, not both." },
    { new LastRunStepCondition { Sequence = "self", Status = "success" }, "lastRun condition requires one of since or within." },
    { new LastRunStepCondition { Sequence = "self", Status = "success", Since = "24:00" }, "lastRun since must be a time of day in HH:mm format (00:00 to 23:59)." },
    { new LastRunStepCondition { Sequence = "self", Status = "success", Within = "400.00:00:00" }, "lastRun within must be a duration more than zero and not more than 366 days, in hh:mm:ss or d.hh:mm:ss format." }
  };

  private static LastRunStepCondition GoodLastRun() => new() { Sequence = "self", Status = "success", Since = "11:00" };

  [Theory]
  [MemberData(nameof(BadLastRunConditions))]
  public void ALastRunLeafAtTheRootGivesItsMessageWithThePrefix(LastRunStepCondition condition, string tail) {
    Validate(condition).Should().ContainSingle().Which.Should().Be($"Step 'S' condition at $: {tail}");
  }

  [Theory]
  [MemberData(nameof(BadLastRunConditions))]
  public void ALastRunChildGivesItsMessageAtItsOwnPath(LastRunStepCondition condition, string tail) {
    foreach (var composite in new CompositeStepCondition[] {
      new AllStepCondition { Children = new SequenceStepCondition[] { Image(), condition } },
      new AnyStepCondition { Children = new SequenceStepCondition[] { Image(), condition } },
      new NoneStepCondition { Children = new SequenceStepCondition[] { Image(), condition } }
    }) {
      Validate(composite).Should().ContainSingle().Which.Should().Be($"Step 'S' condition at $.children[1]: {tail}");
    }
  }

  [Fact]
  public void ACorrectLastRunPassesAtTheRootAndInAComposite() {
    Validate(GoodLastRun()).Should().BeEmpty();
    Validate(new LastRunStepCondition { Sequence = "seq-x", Status = "Cancelled", Within = "1.00:00:00", Negate = true }).Should().BeEmpty();
    Validate(new AnyStepCondition { Children = new SequenceStepCondition[] { GoodLastRun(), Image() } }).Should().BeEmpty();
  }

  [Fact]
  public void TheCompositeLimitsStillApplyWithLastRunChildren() {
    var sixteen = Enumerable.Range(0, 16).Select(_ => (SequenceStepCondition)GoodLastRun()).ToArray();
    Validate(new AllStepCondition { Children = sixteen }).Should().BeEmpty();

    var seventeen = Enumerable.Range(0, 17).Select(_ => (SequenceStepCondition)GoodLastRun()).ToArray();
    Validate(new AllStepCondition { Children = seventeen }).Should().ContainSingle()
      .Which.Should().Contain("allows at most 16 children");

    SequenceStepCondition deep = GoodLastRun();
    for (var level = 1; level < 5; level++) deep = new AllStepCondition { Children = new[] { deep } };
    Validate(deep).Should().ContainSingle().Which.Should().Contain("maximum depth of 4");
  }

  [Fact]
  public void ImageVisibleAndCommandOutcomeRootLeavesKeepTheirCurrentSingleMessage() {
    var steps = new[] {
      Action("s1", new ImageVisibleStepCondition { ImageId = "" }),
      Action("s2", new CommandOutcomeStepCondition { StepRef = "s1", ExpectedState = "maybe" })
    };

    var errors = new SequenceStepValidationService().Validate(steps);

    errors.Should().BeEquivalentTo(
      "Step 's1' imageVisible condition requires imageId.",
      "Step 's2' commandOutcome expectedState must be one of success|failed|skipped|break|no_break.");
  }

  private static SequenceStep Action(string stepId, SequenceStepCondition? condition = null, int order = 0) => new() {
    Order = order,
    StepId = stepId,
    Action = new SequenceActionPayload { Type = "tap", Parameters = { ["x"] = 1, ["y"] = 1 } },
    Condition = condition
  };

  private static SequenceStep BreakStep(string stepId, SequenceStepCondition condition) =>
    new() { StepId = stepId, StepType = SequenceStepType.Break, BreakCondition = condition };

  /// <summary>One sequence for each of the six slots that accept a step condition, with the step label the message names.</summary>
  private static IEnumerable<(string Slot, string Label, SequenceStep[] Steps)> SixSlots(SequenceStepCondition bad) {
    yield return ("step condition", "s1", new[] { Action("s1", bad) });

    var withBreak = Action("s1");
    withBreak.BreakCondition = bad;
    yield return ("Break condition", "s1", new[] { withBreak });

    yield return ("If condition", "if1", new[] {
      new SequenceStep { StepId = "if1", StepType = SequenceStepType.If, If = new IfConfig { Condition = bad }, Body = new[] { Action("then1") } }
    });

    yield return ("loop condition", "loop1", new[] {
      new SequenceStep { StepId = "loop1", StepType = SequenceStepType.Loop, Loop = new WhileLoopConfig { MaxIterations = 2, Condition = bad }, Body = new[] { Action("body1") } }
    });

    yield return ("loop-body Break condition", "brk", new[] {
      new SequenceStep { StepId = "loop1", StepType = SequenceStepType.Loop, Loop = new CountLoopConfig { Count = 2 }, Body = new[] { BreakStep("brk", bad) } }
    });

    yield return ("If-branch Break condition", "ibrk", new[] {
      new SequenceStep {
        StepId = "loop1",
        StepType = SequenceStepType.Loop,
        Loop = new CountLoopConfig { Count = 2 },
        Body = new[] {
          new SequenceStep {
            StepId = "if1",
            StepType = SequenceStepType.If,
            If = new IfConfig { Condition = new ImageVisibleStepCondition { ImageId = "a" } },
            Body = new[] { BreakStep("ibrk", bad) }
          }
        }
      }
    });
  }

  [Theory]
  [MemberData(nameof(BadLastRunConditions))]
  public void EachOfTheSixSlotsReportsALastRunMessageAtTheRoot(LastRunStepCondition condition, string tail) {
    foreach (var (slot, label, steps) in SixSlots(condition)) {
      var errors = new SequenceStepValidationService().Validate(steps);

      errors.Should().ContainSingle(slot).Which.Should().Be($"Step '{label}' condition at $: {tail}", slot);
    }
  }

  [Fact]
  public void EachOfTheSixSlotsAcceptsACorrectLastRun() {
    foreach (var (slot, _, steps) in SixSlots(GoodLastRun())) {
      new SequenceStepValidationService().Validate(steps).Should().BeEmpty(slot);
    }
  }
}
