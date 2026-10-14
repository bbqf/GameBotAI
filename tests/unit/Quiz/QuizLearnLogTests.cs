using System;
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
/// Feature 142 (FR-020a): the log of a quiz-learn step says the outcome and the normalized question, a "replaced" line
/// when an answer changes, and why a step is skipped. It has no secret.
/// </summary>
public sealed class QuizLearnLogTests {
  private readonly InMemoryQuizRepository _repo = new();
  private readonly FakeQuizScreenReader _reader = new();
  private readonly QuizLookupResultKeeper _keeper = new();
  private readonly CapturingLogger<QuizLearnHandler> _log = new();

  private async Task LookupAsync() {
    _reader.Enqueue("Which Planet Is Red?", "Venus", "Mars", "Earth");
    await new QuizLookupHandler(_repo, _reader, new FixedQuizRandom(1), _keeper)
      .ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "emulator-5558", "run-1", CancellationToken.None);
  }

  private Task LearnAsync(GameBot.Domain.Commands.SequenceActionPayload action) =>
    new QuizLearnHandler(_repo, _reader, _keeper, _log).ExecuteAsync(action, "learn-1", "emulator-5558", "run-1", CancellationToken.None);

  [Fact]
  public async Task StoredLogsOutcomeAndQuestion() {
    await LookupAsync();

    await LearnAsync(QuizSteps.LearnByPosition(position: 2));

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome stored").And.Contain("question 'which planet is red'");
  }

  [Fact]
  public async Task ReplacedLogsAReplacedLineAndTheStoredLine() {
    await LookupAsync();
    await LearnAsync(QuizSteps.LearnByPosition(position: 2));

    await LearnAsync(QuizSteps.LearnByPosition(position: 1));

    _log.Lines.Should().HaveCount(3);
    _log.Lines.Should().Contain(l => l.Contains("replaced the stored answer") && l.Contains("'mars' to 'venus'"));
  }

  [Fact]
  public async Task SkippedNoMatchLogsTheOutcomeQuestionAndReason() {
    await LookupAsync();
    _reader.Enqueue("Saturn");

    await LearnAsync(QuizSteps.LearnByRegion());

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome skipped_no_match")
      .And.Contain("question 'which planet is red'").And.Contain("reason");
  }

  [Fact]
  public async Task NoLookupResultLogsWhy() {
    await LearnAsync(QuizSteps.LearnByPosition());

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome skipped_no_match").And.Contain("no lookup result");
  }

  [Fact]
  public async Task SkippedAmbiguousLogsTheOutcome() {
    _reader.Enqueue("Which one", "Same", "Same", "Other");
    await new QuizLookupHandler(_repo, _reader, new FixedQuizRandom(1), _keeper)
      .ExecuteAsync(QuizSteps.Lookup(), "lookup-1", "s", "run-1", CancellationToken.None);

    await LearnAsync(QuizSteps.LearnByPosition(position: 1));

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome skipped_ambiguous").And.Contain("question 'which one'");
  }

  [Fact]
  public async Task ServiceUnavailableLogsTheOutcome() {
    await LookupAsync();
    _repo.Unavailable = true;

    await LearnAsync(QuizSteps.LearnByPosition());

    _log.Lines.Should().ContainSingle().Which.Should().Contain("outcome service_unavailable").And.Contain("question 'which planet is red'");
  }

  [Fact]
  public async Task NoLineHasASecretOrADeviceIdentifier() {
    await LookupAsync();
    await LearnAsync(QuizSteps.LearnByPosition(position: 2));
    await LearnAsync(QuizSteps.LearnByPosition(position: 1));
    _reader.Enqueue("Saturn");
    await LearnAsync(QuizSteps.LearnByRegion());
    _repo.Unavailable = true;
    await LearnAsync(QuizSteps.LearnByPosition());

    _log.Lines.Should().NotBeEmpty();
    _log.Lines.Should().NotContain(l => l.Contains("5558", StringComparison.Ordinal)
      || l.Contains("token", StringComparison.OrdinalIgnoreCase)
      || l.Contains("password", StringComparison.OrdinalIgnoreCase)
      || l.Contains("secret", StringComparison.OrdinalIgnoreCase)
      || l.Contains("Bearer", StringComparison.OrdinalIgnoreCase));
  }
}
