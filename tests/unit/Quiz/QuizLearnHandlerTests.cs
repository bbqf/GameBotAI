using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Quiz;
using GameBot.Domain.Services;
using GameBot.Service.Services.Quiz;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Quiz;

/// <summary>Feature 142: the quiz-learn step logic (User Story 2) and the whole loop.</summary>
public sealed class QuizLearnHandlerTests {
  private readonly InMemoryQuizRepository _repo = new();
  private readonly FakeQuizScreenReader _reader = new();
  private readonly QuizLookupResultKeeper _keeper = new();

  private QuizLearnHandler Learn() => new(_repo, _reader, _keeper);

  private QuizLookupHandler Lookup(int guess = 1) => new(_repo, _reader, new FixedQuizRandom(guess), _keeper);

  private async Task LookupAsync(string question, params string[] answers) {
    _reader.Enqueue(new[] { question }.Concat(answers).ToArray());
    await Lookup().ExecuteAsync(QuizSteps.Lookup(answers: answers.Length), "lookup-1", "s", "run-1", CancellationToken.None);
  }

  private Task<ActionDispatchResult> LearnAsync(SequenceActionPayload action, string runKey = "run-1") =>
    Learn().ExecuteAsync(action, "learn-1", "s", runKey, CancellationToken.None);

  [Fact]
  public async Task LearnByPositionStoresTheAnswerOfThatPosition() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");

    var result = await LearnAsync(QuizSteps.LearnByPosition(position: 2));

    result.Outcome.Should().Be("stored");
    _repo.Records.Should().ContainSingle().Which.Should().Match<QuizRecord>(r =>
      r.Question == "which planet is red" && r.Answer == "mars" && r.Store == "pns-quiz" && r.Game == "pns" && r.Category == "radio");
  }

  [Fact]
  public async Task LearnByRegionReadsTheScreenAndStoresTheMatchedAnswerText() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");
    _reader.Enqueue("MARS!");

    var result = await LearnAsync(QuizSteps.LearnByRegion());

    result.Outcome.Should().Be("stored");
    _repo.Records.Should().ContainSingle().Which.Answer.Should().Be("mars");
  }

  [Fact]
  public async Task LearnByRegionWithASmallOcrErrorStillMatchesOneAnswer() {
    await LookupAsync("Which planet is red?", "Venus", "Mercury", "Earth");
    _reader.Enqueue("Mercurv");

    var result = await LearnAsync(QuizSteps.LearnByRegion());

    result.Outcome.Should().Be("stored");
    _repo.Records.Should().ContainSingle().Which.Answer.Should().Be("mercury", "the stored text is the matched lookup answer");
  }

  [Fact]
  public async Task ACorrectTextThatMatchesNoAnswerIsSkipped() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");
    _reader.Enqueue("Saturn");

    var result = await LearnAsync(QuizSteps.LearnByRegion());

    result.Outcome.Should().Be("skipped_no_match");
    _repo.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task ACorrectTextThatMatchesTwoAnswersIsAmbiguous() {
    await LookupAsync("Which one", "Answer one", "Answer one", "Other");

    var result = await LearnAsync(QuizSteps.LearnByPosition(position: 1));

    result.Outcome.Should().Be("skipped_ambiguous");
    _repo.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task APositionAboveTheNumberOfAnswersIsSkipped() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");

    var result = await LearnAsync(QuizSteps.LearnByPosition(position: 4));

    result.Outcome.Should().Be("skipped_no_match");
    result.Message.Should().Contain("above");
  }

  [Fact]
  public async Task AnUnreadableCorrectRegionIsSkipped() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");
    _reader.Enqueue("  ");
    (await LearnAsync(QuizSteps.LearnByRegion())).Outcome.Should().Be("skipped_no_match");

    _reader.EnqueueFailure();
    (await LearnAsync(QuizSteps.LearnByRegion())).Outcome.Should().Be("skipped_no_match");
  }

  [Fact]
  public async Task NoLookupResultGivesSkippedNoMatchAndSaysWhy() {
    var result = await LearnAsync(QuizSteps.LearnByPosition());

    result.Outcome.Should().Be("skipped_no_match");
    result.Message.Should().Contain("no lookup result").And.Contain("lookup-1");
    _repo.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task AnUnreadableLookupLeavesNoResultToLearnFrom() {
    _reader.Enqueue(" ", "A", "B", "C");
    await Lookup().ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "s", "run-1", CancellationToken.None);

    (await LearnAsync(QuizSteps.LearnByPosition())).Outcome.Should().Be("skipped_no_match");
  }

  [Fact]
  public async Task AResultOfAnotherRunIsNotUsed() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");

    (await LearnAsync(QuizSteps.LearnByPosition(), runKey: "run-2")).Outcome.Should().Be("skipped_no_match");
  }

  [Fact]
  public async Task AnUnusableStoreGivesServiceUnavailable() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");
    _repo.Unavailable = true;

    var result = await LearnAsync(QuizSteps.LearnByPosition());

    result.Outcome.Should().Be("service_unavailable");
  }

  [Fact]
  public async Task LearningTheSameAnswerTwiceGivesOneRecord() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");

    await LearnAsync(QuizSteps.LearnByPosition());
    var second = await LearnAsync(QuizSteps.LearnByPosition());

    second.Outcome.Should().Be("stored");
    _repo.Records.Should().HaveCount(1);
  }

  [Fact]
  public async Task ANewAnswerReplacesTheOldOne() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");
    await LearnAsync(QuizSteps.LearnByPosition(position: 2));

    var result = await LearnAsync(QuizSteps.LearnByPosition(position: 1));

    result.Outcome.Should().Be("stored");
    result.Message.Should().Contain("replaced");
    _repo.Records.Should().ContainSingle().Which.Answer.Should().Be("venus");
  }

  [Fact]
  public async Task AGuessIsNotStoredWithoutALearnStep() {
    await LookupAsync("Which planet is red?", "Venus", "Mars", "Earth");

    _repo.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task AnInvalidPayloadGivesFailed() {
    var action = new SequenceActionPayload { Type = ActionTypes.QuizLearn };

    var result = await LearnAsync(action);

    result.Outcome.Should().Be("failed");
  }

  [Fact]
  public async Task TheWholeLoopGuessLearnThenKnown() {
    // First visit: unknown question, a guess (position 1).
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");
    var first = await Lookup(guess: 1).ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "s", "run-1", CancellationToken.None);
    first.Outcome.Should().Be("pick_1");
    first.Message.Should().StartWith("guess");

    // The author learns the right answer: position 2.
    (await LearnAsync(QuizSteps.LearnByPosition(position: 2))).Outcome.Should().Be("stored");

    // Next visit, new run, answers in a new order: the known answer is picked.
    _reader.Enqueue("Which planet is red?", "Earth", "Venus", "Mars");
    var second = await Lookup(guess: 1).ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "s", "run-2", CancellationToken.None);
    second.Outcome.Should().Be("pick_3");
    second.Message.Should().StartWith("known");
  }
}
