using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Service.Services.Quiz;
using GameBot.UnitTests.Notifications;
using Xunit;

#pragma warning disable CA2007, CA1861

namespace GameBot.UnitTests.Quiz;

/// <summary>
/// Feature 142 (FR-020a): the log of a quiz-lookup step says the outcome, known or guess, the normalized question, and
/// the picked position. It has no credential, token, or device identifier. The test logger gets every line, so no
/// logging gate hides a line.
/// </summary>
public sealed class QuizLookupLogTests {
  private readonly InMemoryQuizRepository _repo = new();
  private readonly FakeQuizScreenReader _reader = new();
  private readonly CapturingLogger<QuizLookupHandler> _log = new();

  private QuizLookupHandler Handler(int guess = 2) => new(_repo, _reader, new FixedQuizRandom(guess), new QuizLookupResultKeeper(), _log);

  private Task RunAsync(string session = "emulator-5558") =>
    Handler().ExecuteAsync(QuizSteps.Lookup(), "lookup-1", session, "run-1", CancellationToken.None);

  [Fact]
  public async Task KnownPickLogsOutcomeKnownQuestionAndPosition() {
    await _repo.UpsertAsync("pns-quiz", "pns", "radio", "Which planet is red?", "Mars");
    _reader.Enqueue("Which Planet Is Red?", "Venus", "Mars", "Earth");

    await RunAsync();

    var line = _log.Lines.Should().ContainSingle().Subject;
    line.Should().Contain("outcome pick_2").And.Contain("known")
      .And.Contain("question 'which planet is red'").And.Contain("picked position 2").And.Contain("lookup-1");
    line.Should().NotContain("guess");
  }

  [Fact]
  public async Task GuessPickLogsOutcomeGuessQuestionAndPosition() {
    _reader.Enqueue("A new question", "One", "Two", "Three");

    await RunAsync();

    var line = _log.Lines.Should().ContainSingle().Subject;
    line.Should().Contain("outcome pick_2").And.Contain("guess")
      .And.Contain("question 'a new question'").And.Contain("picked position 2");
  }

  [Fact]
  public async Task UnreadableLogsTheOutcome() {
    _reader.Enqueue("Question", "One", " ", "Three");

    await RunAsync();

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome unreadable").And.Contain("picked position 0");
  }

  [Fact]
  public async Task ServiceUnavailableLogsTheOutcomeAndTheQuestion() {
    _repo.Unavailable = true;
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");

    await RunAsync();

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome service_unavailable")
      .And.Contain("question 'which planet is red'");
  }

  [Fact]
  public async Task NoLineHasASecretOrADeviceIdentifier() {
    await _repo.UpsertAsync("pns-quiz", "pns", "radio", "Which planet is red?", "Mars");
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");
    await RunAsync("emulator-5558");
    _reader.Enqueue("New question", "A", "B", "C");
    await RunAsync("emulator-5558");
    _reader.Enqueue(" ", "A", "B", "C");
    await RunAsync("emulator-5558");
    _repo.Unavailable = true;
    _reader.Enqueue("Which planet is red?", "Venus", "Mars", "Earth");
    await RunAsync("emulator-5558");

    _log.Lines.Should().HaveCount(4);
    _log.Lines.Should().NotContain(l => l.Contains("emulator-5558") || l.Contains("5558")
      || l.Contains("token", System.StringComparison.OrdinalIgnoreCase)
      || l.Contains("password", System.StringComparison.OrdinalIgnoreCase)
      || l.Contains("secret", System.StringComparison.OrdinalIgnoreCase)
      || l.Contains("Bearer", System.StringComparison.OrdinalIgnoreCase));
  }
}
