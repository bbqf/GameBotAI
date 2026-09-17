using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services;
using Xunit;

namespace GameBot.UnitTests.Sequences;

/// <summary>
/// The two save-time rules governing a <c>commandOutcome</c> condition's <c>stepRef</c> — it must
/// <em>resolve</em> to a step somewhere in the sequence tree, and that step must be <em>prior</em>
/// in authored order — asserted in every condition variant and every slot that validates conditions
/// (feature 103, issue #193).
/// <para>
/// Why this file exists. Issue #193 reported that a <c>stepRef</c> could only name a top-level step
/// and that a nested one was rejected with <c>400 "references unknown prior step"</c>, and feature
/// 081 was recorded as having fixed it. Nobody re-measured. Measuring found the widening real for a
/// condition written directly on a step guard, and three things that had never been true:
/// a reference reached through a composite was not resolved or ordered <em>at all</em>; a reference
/// written directly in an <c>If</c> condition was not either, so feature 081's own acceptance
/// scenario for that slot passed vacuously; and the <c>If</c> slot's message still named three of
/// the five accepted <c>expectedState</c> values.
/// </para>
/// <para>
/// Everything is driven through the public <see cref="SequenceStepValidationService.Validate"/>
/// rather than the validators underneath, so each test asserts what an author actually experiences
/// at save time. The valid-reference tests are as load-bearing as the rejections: widening who gets
/// checked must not narrow what passes.
/// </para>
/// </summary>
public sealed class ConditionReferenceScopeValidationTests {
  private static readonly SequenceStepValidationService Svc = new();

  private static IReadOnlyList<string> Validate(params SequenceStep[] steps) =>
    Svc.Validate(steps.ToList());

  private static string Joined(IEnumerable<string> errors) => string.Join(" | ", errors);

  private static SequenceStep Action(string stepId) => new() {
    Order = 0,
    StepId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = "tap" }
  };

  private static SequenceStep Guarded(string stepId, SequenceStepCondition condition) {
    var step = Action(stepId);
    step.Condition = condition;
    return step;
  }

  private static CommandOutcomeStepCondition Ref(string stepRef, string expectedState = "success") =>
    new() { StepRef = stepRef, ExpectedState = expectedState };

  private static ImageVisibleStepCondition Image(string imageId = "marker") => new() { ImageId = imageId };

  /// <summary>Wraps <paramref name="child"/> in the composite named by <paramref name="rule"/>.</summary>
  private static CompositeStepCondition Composite(string rule, params SequenceStepCondition[] children) => rule switch {
    "all" => new AllStepCondition { Children = children },
    "any" => new AnyStepCondition { Children = children },
    _ => new NoneStepCondition { Children = children }
  };

  private static SequenceStep BreakStep(string stepId, SequenceStepCondition condition) => new() {
    Order = 0,
    StepId = stepId,
    StepType = SequenceStepType.Break,
    BreakCondition = condition
  };

  private static SequenceStep CountLoop(string stepId, params SequenceStep[] body) => new() {
    Order = 0,
    StepId = stepId,
    StepType = SequenceStepType.Loop,
    Loop = new CountLoopConfig { Count = 2 },
    Body = body
  };

  private static SequenceStep IfStep(string stepId, SequenceStepCondition condition, params SequenceStep[] body) => new() {
    Order = 0,
    StepId = stepId,
    StepType = SequenceStepType.If,
    If = new IfConfig { Condition = condition },
    Body = body.Length == 0 ? new[] { Action($"{stepId}-then") } : body
  };

  /// <summary>
  /// A loop whose body ends in a <c>Break</c>, so a later condition has a nested step to reference.
  /// Positions in the authored walk: loop 0, probe 1, the break 2.
  /// </summary>
  private static SequenceStep LoopWithNestedBreak(string loopId = "loop1", string breakId = "nested-break") =>
    CountLoop(loopId, Action("probe"), BreakStep(breakId, Image("done-marker")));

  // ---------- slot: a step's own guard, condition written directly ----------
  // These passed before feature 103. They are the widening issue #193 asked about, and they are
  // asserted here rather than left resting on feature 081's suite, because FR-006 covers every
  // variant and this is one of them.

  [Fact]
  public void ADirectStepGuardReferenceToAnAbsentStepIsRejected() {
    var errors = Validate(Action("first"), Guarded("gate", Ref("no-such-step")));

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'gate' commandOutcome references unknown prior step 'no-such-step'.");
  }

  [Fact]
  public void ADirectStepGuardReferenceToALaterStepIsRejected() {
    // Reachable, but authored after the condition that asks about it: ordering is enforced
    // independently of resolution, and must stay that way.
    var errors = Validate(Guarded("gate", Ref("later")), Action("later"));

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'gate' commandOutcome stepRef 'later' must reference a prior step.");
  }

  [Fact]
  public void ADirectStepGuardReferenceToAStepNestedInAnEarlierLoopIsAccepted() {
    // The headline of issue #193: before feature 081 this was 400 "references unknown prior step".
    // Both halves of the reported ceiling appear here at once — the reference reaches into a loop
    // body, and expectedState is "break".
    Validate(LoopWithNestedBreak(), Guarded("gate", Ref("nested-break", "break")))
      .Should().BeEmpty();
  }

  // ---------- slot: a step's own guard, condition reached through a composite ----------
  // These failed before feature 103: the composite walk checked a child reference for non-emptiness
  // and nothing else, so a dangling or forward reference was accepted at save time and surfaced as
  // a failed run instead of a 400.

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void ACompositeStepGuardReferenceToAnAbsentStepIsRejectedAtItsPath(string rule) {
    var errors = Validate(
      Action("first"),
      Guarded("gate", Composite(rule, Image(), Ref("no-such-step"))));

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'gate' condition at $.children[1]: commandOutcome references unknown prior step 'no-such-step'.");
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void ACompositeStepGuardReferenceToALaterStepIsRejected(string rule) {
    var errors = Validate(
      Guarded("gate", Composite(rule, Ref("later"))),
      Action("later"));

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'gate' condition at $.children[0]: commandOutcome stepRef 'later' must reference a prior step.");
  }

  [Fact]
  public void ACompositeReferenceNestedTwoLevelsDeepIsReportedAtItsFullPath() {
    var condition = Composite("all",
      Image("a"),
      Image("b"),
      Composite("none", Ref("no-such-step")));

    var errors = Validate(Action("first"), Guarded("gate", condition));

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'gate' condition at $.children[2].children[0]: commandOutcome references unknown prior step 'no-such-step'.");
  }

  [Theory]
  [InlineData("all")]
  [InlineData("any")]
  [InlineData("none")]
  public void ACompositeStepGuardReferenceToANestedStepIsAccepted(string rule) {
    // The guard against over-reach: widening who is checked must not change what passes.
    Validate(
      LoopWithNestedBreak(),
      Guarded("gate", Composite(rule, Image(), Ref("nested-break", "break"))))
      .Should().BeEmpty();
  }

  [Fact]
  public void AValidReferenceInsideACompositeWithinACompositeIsAccepted() {
    var condition = Composite("all",
      Image("a"),
      Composite("any", Ref("nested-break", "break"), Image("b")));

    Validate(LoopWithNestedBreak(), Guarded("gate", condition)).Should().BeEmpty();
  }

  // ---------- slot: an If step's condition, written directly ----------
  // Measurement found this slot had never resolved or ordered a reference at all — it validated a
  // condition's shape and stopped. So feature 081's acceptance scenario for the If slot ("an If
  // condition's stepRef naming a nested step is now accepted") passed vacuously: nothing there
  // rejected anything. These are the tests that would have caught that.

  [Fact]
  public void AnIfConditionReferenceToAnAbsentStepIsRejected() {
    var errors = Validate(Action("first"), IfStep("branch", Ref("no-such-step")));

    Joined(errors).Should().Contain("Step 'branch' commandOutcome references unknown prior step 'no-such-step'.");
  }

  [Fact]
  public void AnIfConditionReferenceToALaterStepIsRejected() {
    var errors = Validate(IfStep("branch", Ref("later")), Action("later"));

    Joined(errors).Should().Contain("Step 'branch' commandOutcome stepRef 'later' must reference a prior step.");
  }

  [Fact]
  public void AnIfConditionReferenceToAStepInItsOwnBodyIsRejectedAsNotPrior() {
    // The body is authored after the condition that decides whether to enter it, so asking about a
    // step inside it is a forward reference. Worth pinning: it is the mistake an author is most
    // likely to make in this slot, and it was silently accepted before.
    var errors = Validate(IfStep("branch", Ref("then-step"), Action("then-step")));

    Joined(errors).Should().Contain("Step 'branch' commandOutcome stepRef 'then-step' must reference a prior step.");
  }

  [Fact]
  public void AnIfConditionReferenceToAStepNestedInAnEarlierLoopIsAccepted() {
    Validate(LoopWithNestedBreak(), IfStep("branch", Ref("nested-break", "break")))
      .Should().BeEmpty();
  }

  [Fact]
  public void AnIfConditionReferenceReachedThroughACompositeIsRejectedAtItsPath() {
    var errors = Validate(
      Action("first"),
      IfStep("branch", Composite("all", Image(), Ref("no-such-step"))));

    Joined(errors).Should().Contain("Step 'branch' condition at $.children[1]: commandOutcome references unknown prior step 'no-such-step'.");
  }

  // ---------- the If slot's accepted-outcome-state message ----------

  [Fact]
  public void AnIfConditionUnknownExpectedStateMessageNamesAllFiveAcceptedStates() {
    // The validator has accepted break/no_break since feature 081; only this message lagged, telling
    // an author that break was invalid in a slot that accepts it. Issue #193's half-2 complaint
    // surviving verbatim as a message after the behaviour was fixed.
    var errors = Validate(Action("first"), IfStep("branch", Ref("first", "maybe")));

    errors.Should().ContainSingle()
      .Which.Should().Be("Step 'branch' commandOutcome expectedState must be one of success|failed|skipped|break|no_break.");
  }

  [Theory]
  [InlineData("break")]
  [InlineData("no_break")]
  public void AnIfConditionAcceptsTheBreakOutcomeStates(string expectedState) {
    Validate(LoopWithNestedBreak(), IfStep("branch", Ref("nested-break", expectedState)))
      .Should().BeEmpty();
  }

  // ---------- slot: a Break step's condition, in a loop body and in an If branch ----------

  [Fact]
  public void ACompositeBreakConditionReferenceToAnAbsentStepIsRejected() {
    var loop = CountLoop("loop1",
      Action("probe"),
      BreakStep("brk", Composite("any", Ref("no-such-step"))));

    Joined(Validate(loop))
      .Should().Contain("Step 'brk' condition at $.children[0]: commandOutcome references unknown prior step 'no-such-step'.");
  }

  [Fact]
  public void ACompositeBreakConditionInsideAnIfBranchReferenceToAnAbsentStepIsRejected() {
    var branchBreak = BreakStep("brk", Composite("all", Ref("no-such-step")));
    var loop = CountLoop("loop1", IfStep("branch", Image("q"), branchBreak));

    Joined(Validate(loop))
      .Should().Contain("Step 'brk' condition at $.children[0]: commandOutcome references unknown prior step 'no-such-step'.");
  }

  // ---------- slot: a while / repeat-until loop condition ----------

  [Theory]
  [InlineData("while")]
  [InlineData("repeatUntil")]
  public void ACompositeLoopConditionReferenceToAnAbsentStepIsRejected(string loopType) {
    LoopConfig loop = loopType == "while"
      ? new WhileLoopConfig { Condition = Composite("all", Ref("no-such-step")), MaxIterations = 3 }
      : new RepeatUntilLoopConfig { Condition = Composite("all", Ref("no-such-step")), MaxIterations = 3 };

    var step = new SequenceStep {
      Order = 0,
      StepId = "loop1",
      StepType = SequenceStepType.Loop,
      Loop = loop,
      Body = new[] { Action("b1") }
    };

    Joined(Validate(step))
      .Should().Contain("Step 'loop1' condition at $.children[0]: commandOutcome references unknown prior step 'no-such-step'.");
  }

  // ---------- the inherited D-006 boundary: bare leaves in a loop condition ----------

  [Theory]
  [InlineData("while")]
  [InlineData("repeatUntil")]
  public void ABareLeafReferenceInALoopConditionIsStillNotValidated(string loopType) {
    // Feature 088 decision D-006 deliberately left leaf conditions in these two slots unvalidated,
    // because newly checking them would reject sequences that save today. Feature 103 does not
    // reverse that: a composite in this slot IS checked (test above) while a bare leaf is not, and
    // that asymmetry is inherited, asserted, and documented rather than quietly changed.
    LoopConfig loop = loopType == "while"
      ? new WhileLoopConfig { Condition = Ref("no-such-step"), MaxIterations = 3 }
      : new RepeatUntilLoopConfig { Condition = Ref("no-such-step"), MaxIterations = 3 };

    var step = new SequenceStep {
      Order = 0,
      StepId = "loop1",
      StepType = SequenceStepType.Loop,
      Loop = loop,
      Body = new[] { Action("b1") }
    };

    Validate(step).Should().BeEmpty();
  }
}
