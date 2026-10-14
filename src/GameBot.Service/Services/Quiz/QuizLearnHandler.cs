using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;
using GameBot.Domain.Quiz;
using GameBot.Domain.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Service.Services.Quiz;

/// <summary>
/// Runs a <c>quiz-learn</c> step (feature 142): stores the correct answer of an earlier <c>quiz-lookup</c> step of the
/// same run. The correct text comes from a position of the lookup answers, or from a screen region. The text must
/// reach the similarity limit of the lookup for exactly one answer (research R4a). The stored text is the matched
/// answer text of the lookup. Outcomes: <c>stored</c>, <c>skipped_no_match</c>, <c>skipped_ambiguous</c>,
/// <c>service_unavailable</c>.
/// </summary>
internal sealed partial class QuizLearnHandler {
  private readonly IQuizAnswerRepository _repository;
  private readonly IQuizScreenReader _reader;
  private readonly QuizLookupResultKeeper _keeper;
  private readonly ILogger _logger;

  public QuizLearnHandler(
      IQuizAnswerRepository repository,
      IQuizScreenReader reader,
      QuizLookupResultKeeper keeper,
      ILogger<QuizLearnHandler>? logger = null) {
    _repository = repository;
    _reader = reader;
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
    if (!QuizLearnPayload.TryRead(action, out var payload, out var error) || payload is null) {
      return new ActionDispatchResult("failed", $"quiz-learn step is not valid: {error}");
    }

    var lookup = _keeper.Get(runKey, payload.LookupStepId);
    if (lookup is null) {
      return Skipped(stepId, QuizOutcomes.SkippedNoMatch, string.Empty,
        $"no lookup result for step '{payload.LookupStepId}' (the lookup did not run, or it gave no pick)");
    }

    var correctText = ReadCorrectText(payload, lookup, sessionId, out var textProblem);
    if (correctText is null) {
      return Skipped(stepId, QuizOutcomes.SkippedNoMatch, lookup.Question, textProblem!);
    }

    var matches = new List<int>();
    for (var i = 0; i < lookup.Answers.Count; i++) {
      if (QuizSimilarity.Score(correctText, lookup.Answers[i], lookup.Similarity) >= lookup.Similarity) {
        matches.Add(i);
      }
    }

    if (matches.Count == 0) {
      return Skipped(stepId, QuizOutcomes.SkippedNoMatch, lookup.Question, "the correct text matches no answer of the lookup");
    }

    if (matches.Count > 1) {
      return Skipped(stepId, QuizOutcomes.SkippedAmbiguous, lookup.Question, "the correct text matches more than one answer of the lookup");
    }

    var answerText = lookup.Answers[matches[0]];
    QuizUpsertResult upsert;
    try {
      upsert = await _repository.UpsertAsync(lookup.Store, lookup.Game, lookup.Category, lookup.Question, answerText, ct).ConfigureAwait(false);
    }
    catch (QuizStoreUnavailableException ex) {
      Log.Learn(_logger, stepId, QuizOutcomes.ServiceUnavailable, lookup.Question, ex);
      return new ActionDispatchResult(QuizOutcomes.ServiceUnavailable, $"the quiz store cannot be used; question '{lookup.Question}'");
    }

    if (upsert.Kind == QuizUpsertKind.Replaced) {
      Log.Replaced(_logger, stepId, lookup.Question, upsert.PreviousAnswer ?? string.Empty, answerText);
      Log.Learn(_logger, stepId, QuizOutcomes.Stored, lookup.Question, null);
      return new ActionDispatchResult(QuizOutcomes.Stored, $"replaced the stored answer; question '{lookup.Question}'");
    }

    Log.Learn(_logger, stepId, QuizOutcomes.Stored, lookup.Question, null);
    return new ActionDispatchResult(QuizOutcomes.Stored, $"stored; question '{lookup.Question}'");
  }

  // The normalized correct text, or null with a reason.
  private string? ReadCorrectText(QuizLearnPayload payload, QuizLookupResult lookup, string? sessionId, out string? problem) {
    problem = null;
    if (payload.CorrectPosition is { } position) {
      if (position > lookup.Answers.Count) {
        problem = $"correct position {position} is above the {lookup.Answers.Count} answers of the lookup";
        return null;
      }

      return lookup.Answers[position - 1];
    }

    var texts = _reader.ReadRegions(sessionId, new[] { payload.CorrectRegion!.Value });
    var text = texts is { Count: 1 } ? QuizTextNormalizer.Normalize(texts[0]) : string.Empty;
    if (text.Length == 0) {
      problem = "the correct region has no readable text";
      return null;
    }

    return text;
  }

  private ActionDispatchResult Skipped(string stepId, string outcome, string question, string reason) {
    Log.Skipped(_logger, stepId, outcome, question, reason);
    return new ActionDispatchResult(outcome, reason);
  }

  private static partial class Log {
    [LoggerMessage(
      EventId = 14220,
      Level = LogLevel.Information,
      Message = "quiz-learn step {StepId}: outcome {Outcome}, question '{Question}'")]
    public static partial void Learn(ILogger logger, string stepId, string outcome, string question, Exception? exception);

    [LoggerMessage(
      EventId = 14221,
      Level = LogLevel.Information,
      Message = "quiz-learn step {StepId}: replaced the stored answer of question '{Question}' ('{OldAnswer}' to '{NewAnswer}')")]
    public static partial void Replaced(ILogger logger, string stepId, string question, string oldAnswer, string newAnswer);

    [LoggerMessage(
      EventId = 14222,
      Level = LogLevel.Information,
      Message = "quiz-learn step {StepId}: outcome {Outcome}, question '{Question}', reason: {Reason}")]
    public static partial void Skipped(ILogger logger, string stepId, string outcome, string question, string reason);
  }
}
