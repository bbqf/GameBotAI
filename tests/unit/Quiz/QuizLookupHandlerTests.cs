using System;
using System.Collections.Generic;
using System.Linq;
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

/// <summary>Feature 142: the quiz-lookup step logic (User Story 1).</summary>
public sealed class QuizLookupHandlerTests {
  private readonly InMemoryQuizRepository _repo = new();
  private readonly FakeQuizScreenReader _reader = new();
  private readonly FixedQuizRandom _random = new(3);
  private readonly QuizLookupResultKeeper _keeper = new();

  private QuizLookupHandler Handler() => new(_repo, _reader, _random, _keeper);

  private async Task SeedAsync(string question = "Which planet is red?", string answer = "Mars") =>
    await _repo.UpsertAsync("pns-quiz", "pns", "radio", question, answer);

  private Task<ActionDispatchResult> RunAsync(SequenceActionPayload? action = null, string runKey = "run-1") =>
    Handler().ExecuteAsync(action ?? QuizSteps.Lookup(), "lookup-1", "session-1", runKey, CancellationToken.None);

  [Fact]
  public async Task KnownQuestionGivesThePositionOfTheStoredAnswer() {
    await SeedAsync();
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");

    var result = await RunAsync();

    result.Outcome.Should().Be("pick_2");
    result.Message.Should().StartWith("known");
    _random.Calls.Should().Be(0);
    _reader.LastSessionId.Should().Be("session-1");
    _reader.LastRegions.Should().HaveCount(4);
    _reader.Calls.Should().Be(1, "one frame read serves the question and all answers");
  }

  [Fact]
  public async Task ReorderedAnswersGiveTheNewPosition() {
    await SeedAsync();
    _reader.Enqueue("Which planet is red?", "Mars", "Venus", "Earth");

    var result = await RunAsync();

    result.Outcome.Should().Be("pick_1");
  }

  [Fact]
  public async Task AnOcrErrorInTheQuestionAndAnswerStillMatches() {
    await SeedAsync("Which planet is the closest to the sun", "Mercury");
    _reader.Enqueue("Whlch planet is the closest to the sun", "Venus", "Mercurv", "Earth");

    var result = await RunAsync();

    result.Outcome.Should().Be("pick_2");
    result.Message.Should().StartWith("known");
  }

  [Fact]
  public async Task UnknownQuestionGivesARandomGuessAndStoresNothing() {
    _reader.Enqueue("A new question", "One", "Two", "Three");

    var result = await RunAsync();

    result.Outcome.Should().Be("pick_3");
    result.Message.Should().StartWith("guess");
    _random.Calls.Should().Be(1);
    _repo.Records.Should().BeEmpty();
  }

  [Fact]
  public async Task AStoredAnswerThatNoCurrentAnswerMatchesGivesAGuess() {
    await SeedAsync("Which planet is red?", "Mars");
    _reader.Enqueue("Which planet is red?", "Venus", "Jupiter", "Earth");

    var result = await RunAsync();

    result.Outcome.Should().Be("pick_3");
    result.Message.Should().StartWith("guess");
  }

  [Fact]
  public async Task TheGuessStaysInsideTheNumberOfAnswers() {
    _reader.Enqueue("A new question", "One", "Two");
    var handler = new QuizLookupHandler(_repo, _reader, new FixedQuizRandom(99), _keeper);

    var result = await handler.ExecuteAsync(QuizSteps.Lookup(answers: 2), "lookup-1", "s", "run-1", CancellationToken.None);

    result.Outcome.Should().Be("pick_2");
  }

  [Theory]
  [InlineData("   ", "A", "B", "C")]
  [InlineData("?!", "A", "B", "C")]
  [InlineData("Question", "A", " ", "C")]
  [InlineData("Question", "A", "B", "...")]
  public async Task AnEmptyRegionGivesUnreadableAndNoPick(string question, string a1, string a2, string a3) {
    await SeedAsync("Question", "A");
    _reader.Enqueue(question, a1, a2, a3);

    var result = await RunAsync();

    result.Outcome.Should().Be("unreadable");
    _random.Calls.Should().Be(0);
    _keeper.Get("run-1", "lookup-1").Should().BeNull();
  }

  [Fact]
  public async Task AnUnreadableRegionThatIsNullGivesUnreadable() {
    _reader.Enqueue("Question", "A", null, "C");

    (await RunAsync()).Outcome.Should().Be("unreadable");
  }

  [Fact]
  public async Task AnOcrFaultOrNoFrameGivesUnreadable() {
    _reader.EnqueueFailure();

    var result = await RunAsync();

    result.Outcome.Should().Be("unreadable");
    _random.Calls.Should().Be(0);
  }

  [Fact]
  public async Task AReadWithTheWrongNumberOfTextsGivesUnreadable() {
    _reader.Enqueue("Question", "A");

    (await RunAsync()).Outcome.Should().Be("unreadable");
  }

  [Fact]
  public async Task AnUnusableStoreGivesServiceUnavailable() {
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");
    _repo.Unavailable = true;

    var result = await RunAsync();

    result.Outcome.Should().Be("service_unavailable");
    _random.Calls.Should().Be(0);
    _keeper.Get("run-1", "lookup-1").Should().BeNull();
  }

  [Fact]
  public async Task APickKeepsTheLookupResultForALaterLearnStep() {
    _reader.Enqueue("Which Planet Is Red?", "Venus", "Mars", "Earth");

    await RunAsync();

    var kept = _keeper.Get("run-1", "lookup-1");
    kept.Should().NotBeNull();
    kept!.Question.Should().Be("which planet is red");
    kept.Answers.Should().Equal("venus", "mars", "earth");
    kept.Store.Should().Be("pns-quiz");
    kept.Game.Should().Be("pns");
    kept.Category.Should().Be("radio");
    kept.Similarity.Should().Be(0.85);
    kept.Outcome.Should().Be("pick_3");
    _keeper.Get("other-run", "lookup-1").Should().BeNull();
  }

  [Fact]
  public async Task AnUnreadableLookupInALoopForgetsTheOlderResult() {
    _reader.Enqueue("Question one", "A", "B", "C");
    await RunAsync();
    _keeper.Get("run-1", "lookup-1").Should().NotBeNull();
    _reader.Enqueue("  ", "A", "B", "C");

    await RunAsync();

    _keeper.Get("run-1", "lookup-1").Should().BeNull();
  }

  [Fact]
  public async Task TheSimilarityLimitOfTheStepIsUsed() {
    await SeedAsync("Which planet is red?", "Mars");
    _reader.Enqueue("Which planet is red?", "Venus", "Marz", "Earth");

    var strict = await RunAsync(QuizSteps.Lookup(similarity: 1.0));
    _reader.Enqueue("Which planet is red?", "Venus", "Marz", "Earth");
    var loose = await RunAsync(QuizSteps.Lookup(similarity: 0.7));

    strict.Message.Should().StartWith("guess");
    loose.Outcome.Should().Be("pick_2");
    loose.Message.Should().StartWith("known");
  }

  [Fact]
  public async Task AnInvalidPayloadGivesFailedWithoutReadingTheScreen() {
    var action = new SequenceActionPayload { Type = ActionTypes.QuizLookup };

    var result = await RunAsync(action);

    result.Outcome.Should().Be("failed");
    _reader.Calls.Should().Be(0);
  }

  [Fact]
  public async Task AGameOrCategoryOfAnotherStepFindsNoRecord() {
    await SeedAsync();
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");

    var result = await RunAsync(QuizSteps.Lookup(category: "history"));

    result.Message.Should().StartWith("guess");
  }

  // ---- T023: a later commandOutcome condition reads the lookup outcome ----

  private sealed class StubRepo : ISequenceRepository {
    private readonly CommandSequence _sequence;
    public StubRepo(CommandSequence sequence) => _sequence = sequence;
    public Task<CommandSequence?> GetAsync(string id) => Task.FromResult<CommandSequence?>(_sequence);
    public Task<IReadOnlyList<CommandSequence>> ListAsync() => Task.FromResult<IReadOnlyList<CommandSequence>>(new List<CommandSequence> { _sequence });
    public Task<CommandSequence> CreateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<CommandSequence> UpdateAsync(CommandSequence sequence) => Task.FromResult(sequence);
    public Task<bool> DeleteAsync(string id) => Task.FromResult(true);
  }

  private static SequenceStep TapIf(string stepId, string expectedState) => new() {
    Order = 1,
    StepId = stepId,
    CommandId = stepId,
    StepType = SequenceStepType.Action,
    Action = new SequenceActionPayload { Type = ActionTypes.Tap, Parameters = { ["x"] = 1, ["y"] = 1 } },
    Condition = new CommandOutcomeStepCondition { StepRef = "lookup-1", ExpectedState = expectedState }
  };

  private async Task<(List<string> Ran, SequenceExecutionResult Result)> RunSequenceAsync(params SequenceStep[] followers) {
    var lookupStep = new SequenceStep {
      Order = 0,
      StepId = "lookup-1",
      CommandId = "lookup-1",
      StepType = SequenceStepType.Action,
      Action = QuizSteps.Lookup()
    };
    var sequence = new CommandSequence { Id = "seq", Name = "seq" };
    sequence.SetSteps(new[] { lookupStep }.Concat(followers).ToArray());
    var ran = new List<string>();
    var handler = Handler();
    var runner = new SequenceRunner(new StubRepo(sequence));

    var result = await runner.ExecuteAsync(
      "seq",
      (_, _) => Task.CompletedTask,
      actionDispatcher: (action, ct) => {
        if (SequenceRunner.IsQuizAction(action)) {
          return handler.ExecuteAsync(action, QuizStepScope.Current ?? "?", "session-1", "run-1", ct);
        }

        ran.Add(action.Type);
        return Task.FromResult(new ActionDispatchResult("executed", "tap"));
      },
      ct: CancellationToken.None);
    return (ran, result);
  }

  [Fact]
  public async Task AConditionOnPick2RunsTheStepOnlyWhenThePickIsTwo() {
    await SeedAsync();
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");

    var (ran, result) = await RunSequenceAsync(TapIf("tap-2", "pick_2"), TapIf("tap-3", "pick_3"));

    result.Status.Should().Be("Succeeded");
    ran.Should().HaveCount(1, "only the pick_2 condition holds");
  }

  [Fact]
  public async Task AConditionOnUnreadableRunsWhenNothingCouldBeRead() {
    _reader.EnqueueFailure();

    var (ran, result) = await RunSequenceAsync(TapIf("tap-u", "unreadable"), TapIf("tap-2", "pick_2"));

    result.Status.Should().Be("Succeeded");
    ran.Should().HaveCount(1);
  }

  [Fact]
  public async Task AConditionOnSuccessMatchesAPick() {
    await SeedAsync();
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");

    var (ran, _) = await RunSequenceAsync(TapIf("tap-s", "success"));

    ran.Should().HaveCount(1);
  }

  [Fact]
  public async Task AQuizStepNeverStopsTheSequence() {
    _repo.Unavailable = true;
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");

    var (ran, result) = await RunSequenceAsync(TapIf("tap-x", "service_unavailable"));

    result.Status.Should().Be("Succeeded");
    ran.Should().HaveCount(1);
  }
}
