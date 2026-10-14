using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Quiz;
using GameBot.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Service.Services.Quiz;

/// <summary>
/// Runs a <c>quiz-lookup</c> step (feature 142): reads the question and the answers from one frame, looks the question
/// up in the store, and gives the outcome <c>pick_N</c>, <c>unreadable</c>, or <c>service_unavailable</c>. The step
/// never taps. A known question gives the position of the stored answer text. An unknown question, or a stored
/// answer that no current answer matches, gives a random position (a guess). The step keeps its result for a later
/// <c>quiz-learn</c> step. One log line says the outcome, known or guess, the normalized question, and the position.
/// </summary>
internal sealed partial class QuizLookupHandler {
  private readonly IQuizAnswerRepository _repository;
  private readonly IQuizScreenReader _reader;
  private readonly IQuizRandom _random;
  private readonly QuizLookupResultKeeper _keeper;
  private readonly ILogger _logger;

  public QuizLookupHandler(
      IQuizAnswerRepository repository,
      IQuizScreenReader reader,
      IQuizRandom random,
      QuizLookupResultKeeper keeper,
      ILogger<QuizLookupHandler>? logger = null) {
    _repository = repository;
    _reader = reader;
    _random = random;
    _keeper = keeper;
    _logger = (ILogger?)logger ?? NullLogger.Instance;
  }

  public async Task<ActionDispatchResult> ExecuteAsync(
      SequenceActionPayload action,
      string stepId,
      string? sessionId,
      string runKey,
      CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(action);
    if (!QuizLookupPayload.TryRead(action, out var payload, out var error) || payload is null) {
      _keeper.Forget(runKey, stepId);
      return new ActionDispatchResult("failed", $"quiz-lookup step is not valid: {error}");
    }

    // One frame for the question and all answers.
    var regions = new List<OcrOffsetRegion>(payload.Answers.Count + 1) { payload.Question };
    regions.AddRange(payload.Answers);
    var texts = _reader.ReadRegions(sessionId, regions);
    if (texts is null || texts.Count != regions.Count) {
      return Unreadable(runKey, stepId, string.Empty, "the screen could not be read");
    }

    var question = QuizTextNormalizer.Normalize(texts[0]);
    var answers = new List<string>(payload.Answers.Count);
    for (var i = 1; i < texts.Count; i++) {
      answers.Add(QuizTextNormalizer.Normalize(texts[i]));
    }

    if (question.Length == 0) {
      return Unreadable(runKey, stepId, question, "the question region has no text");
    }

    var emptyAnswer = answers.FindIndex(a => a.Length == 0);
    if (emptyAnswer >= 0) {
      return Unreadable(runKey, stepId, question, $"answer region {emptyAnswer + 1} has no text");
    }

    QuizRecord? record;
    try {
      record = await _repository.FindBestAsync(payload.Store, payload.Game, payload.Category, question, payload.Similarity, ct).ConfigureAwait(false);
    }
    catch (QuizStoreUnavailableException ex) {
      _keeper.Forget(runKey, stepId);
      Log.Lookup(_logger, stepId, QuizOutcomes.ServiceUnavailable, "none", question, 0, ex);
      return new ActionDispatchResult(QuizOutcomes.ServiceUnavailable, $"the quiz store cannot be used; question '{question}'");
    }

    var position = record is null ? 0 : FindAnswerPosition(record.Answer, answers, payload.Similarity);
    var known = position > 0;
    if (!known) {
      position = _random.Pick(1, answers.Count + 1);
    }

    var outcome = QuizOutcomes.Pick(position);
    _keeper.Keep(runKey, new QuizLookupResult {
      StepId = stepId,
      Store = payload.Store,
      Game = payload.Game,
      Category = payload.Category,
      Question = question,
      Answers = answers,
      Similarity = payload.Similarity,
      Outcome = outcome
    });

    var source = known ? "known" : "guess";
    Log.Lookup(_logger, stepId, outcome, source, question, position, null);
    return new ActionDispatchResult(outcome, $"{source}; question '{question}'; picked position {position}");
  }

  // The best answer at or above the limit. A tie gives the first position. 0 when no answer reaches the limit.
  private static int FindAnswerPosition(string storedAnswer, List<string> answers, double limit) {
    var bestPosition = 0;
    var bestScore = double.NegativeInfinity;
    for (var i = 0; i < answers.Count; i++) {
      var score = QuizSimilarity.Score(storedAnswer, answers[i], limit);
      if (score >= limit && score > bestScore) {
        bestScore = score;
        bestPosition = i + 1;
      }
    }

    return bestPosition;
  }

  private ActionDispatchResult Unreadable(string runKey, string stepId, string question, string reason) {
    _keeper.Forget(runKey, stepId);
    Log.Lookup(_logger, stepId, QuizOutcomes.Unreadable, "none", question, 0, null);
    return new ActionDispatchResult(QuizOutcomes.Unreadable, $"{reason}; no pick was made");
  }

  private static partial class Log {
    [LoggerMessage(
      EventId = 14210,
      Level = LogLevel.Information,
      Message = "quiz-lookup step {StepId}: outcome {Outcome}, {Source}, question '{Question}', picked position {Position}")]
    public static partial void Lookup(ILogger logger, string stepId, string outcome, string source, string question, int position, Exception? exception);
  }
}
