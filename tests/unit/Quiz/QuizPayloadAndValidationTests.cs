using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Quiz;
using GameBot.Domain.Services;
using GameBot.Service.Services.Quiz;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Quiz;

/// <summary>Feature 142: payload reading, save-time validation, outcome names, the keeper, and the step scope.</summary>
public sealed class QuizPayloadAndValidationTests {
  private static SequenceStep Step(string id, SequenceActionPayload action, int order = 0) => new() {
    Order = order,
    StepId = id,
    CommandId = id,
    StepType = SequenceStepType.Action,
    Action = action
  };

  private static IReadOnlyList<string> Validate(params SequenceStep[] steps) =>
    new SequenceStepValidationService().Validate(steps);

  private static SequenceActionPayload FromJson(string type, string json) {
    var action = new SequenceActionPayload { Type = type };
    foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject()) {
      action.Parameters[property.Name] = property.Value.Clone();
    }

    return action;
  }

  // ---- lookup payload ----

  [Fact]
  public void ALookupPayloadOfPlainValuesIsRead() {
    var ok = QuizLookupPayload.TryRead(QuizSteps.Lookup(similarity: 0.9), out var payload, out var error);

    ok.Should().BeTrue();
    error.Should().BeNull();
    payload!.Game.Should().Be("pns");
    payload.Category.Should().Be("radio");
    payload.Store.Should().Be("pns-quiz");
    payload.Answers.Should().HaveCount(3);
    payload.Question.Width.Should().Be(400);
    payload.Similarity.Should().Be(0.9);
  }

  [Fact]
  public void ALookupPayloadFromJsonIsReadAndTheSimilarityDefaultsTo085() {
    var action = FromJson("quiz-lookup", """
      {"game":"pns","category":"radio","store":"s",
       "question":{"x":1,"y":2,"width":3,"height":4},
       "answers":[{"x":1,"y":2,"width":3,"height":4},{"x":"5","y":6,"width":7.0,"height":8}]}
      """);

    QuizLookupPayload.TryRead(action, out var payload, out _).Should().BeTrue();
    payload!.Similarity.Should().Be(0.85);
    payload.Answers[1].X.Should().Be(5);
    payload.Answers[1].Width.Should().Be(7);
  }

  [Theory]
  [InlineData("game")]
  [InlineData("category")]
  [InlineData("store")]
  public void AMissingOrBlankTextFieldIsRejected(string field) {
    var missing = QuizSteps.Lookup();
    missing.Parameters.Remove(field);
    var blank = QuizSteps.Lookup();
    blank.Parameters[field] = "   ";
    var notText = QuizSteps.Lookup();
    notText.Parameters[field] = 5;

    foreach (var action in new[] { missing, blank, notText }) {
      QuizLookupPayload.Validate(action).Should().Contain(e => e.Contains($"'{field}'"));
      QuizLookupPayload.TryRead(action, out var payload, out var error).Should().BeFalse();
      payload.Should().BeNull();
      error.Should().Contain(field);
    }
  }

  [Fact]
  public void AnActionThatIsNullIsRejected() {
    QuizLookupPayload.Validate(null).Should().ContainSingle();
    QuizLearnPayload.Validate(null).Should().ContainSingle();
    QuizLookupPayload.TryRead(null, out _, out var error).Should().BeFalse();
    error.Should().Contain("missing");
  }

  [Fact]
  public void TheQuestionRegionRules() {
    var missing = QuizSteps.Lookup();
    missing.Parameters.Remove("question");
    var bad = QuizSteps.Lookup();
    bad.Parameters["question"] = "text";
    var flat = QuizSteps.Lookup();
    flat.Parameters["question"] = new { x = 1, y = 1, width = 0, height = 10 };

    QuizLookupPayload.Validate(missing).Should().Contain(e => e.Contains("'question' region is required"));
    QuizLookupPayload.Validate(bad).Should().Contain(e => e.Contains("'question' must be a region"));
    QuizLookupPayload.Validate(flat).Should().Contain(e => e.Contains("'question' region width and height"));
  }

  [Fact]
  public void TheAnswerRegionRules() {
    var one = QuizSteps.Lookup(answers: 1);
    var none = QuizSteps.Lookup();
    none.Parameters.Remove("answers");
    var notList = QuizSteps.Lookup();
    notList.Parameters["answers"] = "x";
    var badItem = QuizSteps.Lookup();
    badItem.Parameters["answers"] = new List<object> { new { x = 1 }, QuizSteps.RegionJson(10) };
    var flatItem = QuizSteps.Lookup();
    flatItem.Parameters["answers"] = new List<object> { new { x = 1, y = 1, width = 5, height = -1 }, QuizSteps.RegionJson(10) };

    QuizLookupPayload.Validate(one).Should().Contain(e => e.Contains("2 or more"));
    QuizLookupPayload.Validate(none).Should().Contain(e => e.Contains("'answers'"));
    QuizLookupPayload.Validate(notList).Should().Contain(e => e.Contains("'answers'"));
    QuizLookupPayload.Validate(badItem).Should().Contain(e => e.Contains("item 1"));
    QuizLookupPayload.Validate(flatItem).Should().Contain(e => e.Contains("item 1") && e.Contains("more than 0"));
  }

  [Theory]
  [InlineData(-0.1)]
  [InlineData(1.5)]
  [InlineData(double.NaN)]
  public void ASimilarityOutsideZeroToOneIsRejected(double similarity) {
    QuizLookupPayload.Validate(QuizSteps.Lookup(similarity: similarity)).Should().Contain(e => e.Contains("'similarity'"));
  }

  [Theory]
  [InlineData(0.0)]
  [InlineData(1.0)]
  [InlineData(0.5)]
  public void ASimilarityFromZeroToOneIsAccepted(double similarity) {
    QuizLookupPayload.Validate(QuizSteps.Lookup(similarity: similarity)).Should().BeEmpty();
  }

  [Fact]
  public void ASimilarityThatIsTextIsRejected() {
    var action = QuizSteps.Lookup();
    action.Parameters["similarity"] = "high";

    QuizLookupPayload.Validate(action).Should().Contain(e => e.Contains("'similarity'"));
  }

  // ---- learn payload ----

  [Fact]
  public void ALearnPayloadByPositionOrRegionIsRead() {
    QuizLearnPayload.TryRead(QuizSteps.LearnByPosition(position: 3), out var byPosition, out _).Should().BeTrue();
    QuizLearnPayload.TryRead(QuizSteps.LearnByRegion(), out var byRegion, out _).Should().BeTrue();

    byPosition!.CorrectPosition.Should().Be(3);
    byPosition.CorrectRegion.Should().BeNull();
    byPosition.LookupStepId.Should().Be("lookup-1");
    byRegion!.CorrectRegion.Should().NotBeNull();
    byRegion.CorrectPosition.Should().BeNull();
  }

  [Fact]
  public void ALearnPayloadNeedsExactlyOneOfRegionOrPosition() {
    var both = QuizSteps.LearnByPosition();
    both.Parameters["correctRegion"] = QuizSteps.RegionJson(1);
    var neither = new SequenceActionPayload { Type = "quiz-learn" };
    neither.Parameters["lookupStepId"] = "lookup-1";

    QuizLearnPayload.Validate(both).Should().Contain(e => e.Contains("exactly one"));
    QuizLearnPayload.Validate(neither).Should().Contain(e => e.Contains("exactly one"));
  }

  [Theory]
  [InlineData(0)]
  [InlineData(-2)]
  public void APositionBelowOneIsRejected(int position) {
    QuizLearnPayload.Validate(QuizSteps.LearnByPosition(position: position)).Should().Contain(e => e.Contains("1 or more"));
  }

  [Fact]
  public void ALearnPayloadWithBadValuesIsRejected() {
    var noId = QuizSteps.LearnByPosition();
    noId.Parameters.Remove("lookupStepId");
    var textPosition = QuizSteps.LearnByPosition();
    textPosition.Parameters["correctPosition"] = "two";
    var badRegion = QuizSteps.LearnByRegion();
    badRegion.Parameters["correctRegion"] = new { x = 1 };
    var flatRegion = QuizSteps.LearnByRegion();
    flatRegion.Parameters["correctRegion"] = new { x = 1, y = 1, width = 0, height = 3 };

    QuizLearnPayload.Validate(noId).Should().Contain(e => e.Contains("'lookupStepId'"));
    QuizLearnPayload.Validate(textPosition).Should().Contain(e => e.Contains("whole number"));
    QuizLearnPayload.Validate(badRegion).Should().Contain(e => e.Contains("'correctRegion' must be a region"));
    QuizLearnPayload.Validate(flatRegion).Should().Contain(e => e.Contains("more than 0"));
    QuizLearnPayload.TryRead(noId, out var payload, out var error).Should().BeFalse();
    payload.Should().BeNull();
    error.Should().NotBeNull();
  }

  // ---- save-time step validation ----

  [Fact]
  public void AValidLookupAndLearnPairPasses() {
    Validate(Step("lookup-1", QuizSteps.Lookup()), Step("learn-1", QuizSteps.LearnByPosition(), 1)).Should().BeEmpty();
  }

  [Fact]
  public void AWrongLookupShapeGivesAnErrorWithTheStepLabel() {
    var action = QuizSteps.Lookup();
    action.Parameters.Remove("category");

    var errors = Validate(Step("lookup-1", action));

    errors.Should().ContainSingle().Which.Should().Contain("lookup-1").And.Contain("quiz-lookup").And.Contain("'category'");
  }

  [Fact]
  public void ALearnWithoutAnEarlierLookupIsRejected() {
    Validate(Step("learn-1", QuizSteps.LearnByPosition("nope"))).Should().Contain(e => e.Contains("must name a quiz-lookup step"));
  }

  [Fact]
  public void ALearnThatNamesAStepOfAnotherTypeIsRejected() {
    var tap = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 1 } };

    Validate(Step("tap-1", tap), Step("learn-1", QuizSteps.LearnByPosition("tap-1"), 1))
      .Should().Contain(e => e.Contains("must name a quiz-lookup step"));
  }

  [Fact]
  public void ALearnBeforeItsLookupIsRejected() {
    Validate(Step("learn-1", QuizSteps.LearnByPosition()), Step("lookup-1", QuizSteps.Lookup(), 1))
      .Should().Contain(e => e.Contains("before this step"));
  }

  [Fact]
  public void ALearnInsideALoopBodyMayNameALookupBeforeTheLoop() {
    var loop = new SequenceStep {
      Order = 1,
      StepId = "loop",
      StepType = SequenceStepType.Loop,
      Loop = new CountLoopConfig { Count = 2 },
      Body = new List<SequenceStep> { Step("learn-1", QuizSteps.LearnByPosition(), 2) }
    };

    Validate(Step("lookup-1", QuizSteps.Lookup()), loop).Should().BeEmpty();
  }

  [Theory]
  [InlineData("pick_1")]
  [InlineData("pick_12")]
  [InlineData("unreadable")]
  [InlineData("service_unavailable")]
  [InlineData("stored")]
  [InlineData("skipped_no_match")]
  [InlineData("skipped_ambiguous")]
  [InlineData("SUCCESS")]
  public void ACommandOutcomeConditionMayNameAQuizOutcome(string expected) {
    var gate = Step("gate", new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 1 } }, 1);
    gate.Condition = new CommandOutcomeStepCondition { StepRef = "lookup-1", ExpectedState = expected };

    Validate(Step("lookup-1", QuizSteps.Lookup()), gate).Should().BeEmpty();
  }

  [Theory]
  [InlineData("pick_0")]
  [InlineData("pick_")]
  [InlineData("pick_x")]
  [InlineData("pick_-1")]
  [InlineData("maybe")]
  public void ACommandOutcomeConditionStillRejectsAnUnknownState(string expected) {
    var gate = Step("gate", new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 1 } }, 1);
    gate.Condition = new CommandOutcomeStepCondition { StepRef = "lookup-1", ExpectedState = expected };

    Validate(Step("lookup-1", QuizSteps.Lookup()), gate).Should().ContainSingle().Which.Should().Contain("expectedState must be one of");
  }

  // ---- outcome names, keeper, scope ----

  [Theory]
  [InlineData("pick_1", true, 1)]
  [InlineData("PICK_7", true, 7)]
  [InlineData("pick_0", false, 0)]
  [InlineData("pick_", false, 0)]
  [InlineData("pick_1a", false, 0)]
  [InlineData("pick_99999999999", false, 0)]
  [InlineData("stored", false, 0)]
  [InlineData(null, false, 0)]
  public void ThePickOutcomeIsParsed(string? outcome, bool ok, int position) {
    QuizOutcomes.TryParsePick(outcome, out var parsed).Should().Be(ok);
    parsed.Should().Be(position);
  }

  [Fact]
  public void OutcomeNamesAreKnownOrNot() {
    QuizOutcomes.Pick(3).Should().Be("pick_3");
    QuizOutcomes.IsQuizOutcome("Unreadable").Should().BeTrue();
    QuizOutcomes.IsQuizOutcome("success").Should().BeFalse();
    QuizOutcomes.IsQuizOutcome(" ").Should().BeFalse();
    QuizOutcomes.IsSuccessful("pick_2").Should().BeTrue();
    QuizOutcomes.IsSuccessful("stored").Should().BeTrue();
    QuizOutcomes.IsSuccessful("unreadable").Should().BeFalse();
    StepOutcomeStates.Matches("pick_2", "success").Should().BeTrue();
    StepOutcomeStates.Matches("unreadable", "success").Should().BeFalse();
    StepOutcomeStates.IsExpectedStateAllowed("pick_2").Should().BeTrue();
    StepOutcomeStates.IsAllowed("pick_2").Should().BeFalse("the fixed list is unchanged");
  }

  [Fact]
  public void TheKeeperKeepsPerRunAndRemovesTheSlotAtTheEnd() {
    var keeper = new QuizLookupResultKeeper();
    var result = new QuizLookupResult { StepId = "lookup-1", Question = "q", Outcome = "pick_1" };

    using (keeper.Open("run-1")) {
      keeper.Keep("run-1", result);
      keeper.Get("run-1", "lookup-1").Should().BeSameAs(result);
      keeper.Get("run-2", "lookup-1").Should().BeNull();
      keeper.OpenSlotCount.Should().Be(1);
    }

    keeper.Get("run-1", "lookup-1").Should().BeNull();
    keeper.OpenSlotCount.Should().Be(0);
  }

  [Fact]
  public void TheKeeperForgetsOneStepAndExpiresOldEntries() {
    var time = new ManualTime();
    var keeper = new QuizLookupResultKeeper(time);
    keeper.Keep("run-1", new QuizLookupResult { StepId = "a" });
    keeper.Keep("run-1", new QuizLookupResult { StepId = "b" });

    keeper.Forget("run-1", "a");
    keeper.Forget("no-run", "a");
    keeper.Get("run-1", "a").Should().BeNull();
    keeper.Get("run-1", "b").Should().NotBeNull();

    time.Advance(QuizLookupResultKeeper.EntryLifetime + TimeSpan.FromSeconds(1));
    keeper.Get("run-1", "b").Should().BeNull();
    keeper.Keep("run-2", new QuizLookupResult { StepId = "c" });
    keeper.OpenSlotCount.Should().Be(1, "the expired slot is purged");
  }

  [Fact]
  public void TheStepScopeIsAmbientOnlyInsideThePush() {
    QuizStepScope.Current.Should().BeNull();

    using (QuizStepScope.Push("lookup-1")) {
      QuizStepScope.Current.Should().Be("lookup-1");
      using (QuizStepScope.Push("inner")) {
        QuizStepScope.Current.Should().Be("inner");
      }

      QuizStepScope.Current.Should().Be("lookup-1");
    }

    QuizStepScope.Current.Should().BeNull();
  }

  private sealed class ManualTime : TimeProvider {
    private DateTimeOffset _now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
  }

  [Fact]
  public async Task AHostWithoutAScreenReadsNothing() {
    var reader = new NullQuizScreenReader();

    reader.ReadRegions("s", new[] { QuizSteps.Region(1) }).Should().BeNull();
    await Task.CompletedTask;
  }

  [Fact]
  public void TheQuizTypesAreInTheCanonicalList() {
    SequenceActionTypes.All.Should().Contain(new[] { "quiz-lookup", "quiz-learn" });
    SequenceRunner.IsServiceLevelAction(QuizSteps.Lookup()).Should().BeTrue();
    SequenceRunner.IsServiceLevelAction(QuizSteps.LearnByPosition()).Should().BeTrue();
    SequenceRunner.IsQuizAction(new SequenceActionPayload { Type = "tap" }).Should().BeFalse();
    SequenceRunner.IsQuizAction(null).Should().BeFalse();
    new ActionPayloadValidationService().SupportedActionTypes.Should().Contain("quiz-lookup").And.Contain("quiz-learn");
  }
}
