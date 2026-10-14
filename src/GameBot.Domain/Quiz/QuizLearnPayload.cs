using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Domain.Quiz;

/// <summary>
/// The typed view of a <c>quiz-learn</c> action (feature 142). The step names an earlier <c>quiz-lookup</c>
/// step and gives the correct answer by position or by screen region (one of the two).
/// </summary>
public sealed class QuizLearnPayload {
  /// <summary>The wire key of the lookup step id.</summary>
  public const string LookupStepIdKey = "lookupStepId";

  /// <summary>The wire key of the correct answer region.</summary>
  public const string CorrectRegionKey = "correctRegion";

  /// <summary>The wire key of the correct answer position.</summary>
  public const string CorrectPositionKey = "correctPosition";

  /// <summary>The id of the <c>quiz-lookup</c> step that this step learns from.</summary>
  public string LookupStepId { get; private set; } = string.Empty;

  /// <summary>The region that shows the correct answer, when the step uses a region.</summary>
  public OcrOffsetRegion? CorrectRegion { get; private set; }

  /// <summary>The position of the correct answer, from 1, when the step uses a position.</summary>
  public int? CorrectPosition { get; private set; }

  /// <summary>Reads the payload. False with the first error when the payload is not valid.</summary>
  public static bool TryRead(SequenceActionPayload? action, out QuizLearnPayload? payload, out string? error) {
    var errors = Parse(action, out payload);
    error = errors.Count > 0 ? errors[0] : null;
    if (errors.Count > 0) {
      payload = null;
      return false;
    }

    return true;
  }

  /// <summary>Returns all save-time errors of the payload. The list is empty when the payload is valid.</summary>
  public static IReadOnlyList<string> Validate(SequenceActionPayload? action) => Parse(action, out _);

  private static List<string> Parse(SequenceActionPayload? action, out QuizLearnPayload? payload) {
    var errors = new List<string>();
    payload = null;
    if (action is null) {
      errors.Add("action payload is missing");
      return errors;
    }

    var lookupStepId = QuizPayloadReader.ReadString(action, LookupStepIdKey);
    if (string.IsNullOrWhiteSpace(lookupStepId)) {
      errors.Add($"'{LookupStepIdKey}' is required and must not be blank");
    }

    var hasRegion = QuizPayloadReader.TryGet(action, CorrectRegionKey, out var regionElement);
    var hasPosition = QuizPayloadReader.TryGet(action, CorrectPositionKey, out var positionElement);
    if (hasRegion == hasPosition) {
      errors.Add($"exactly one of '{CorrectRegionKey}' or '{CorrectPositionKey}' is required");
    }

    OcrOffsetRegion? region = null;
    if (hasRegion) {
      if (!QuizPayloadReader.TryReadRegion(regionElement, out var parsed)) {
        errors.Add($"'{CorrectRegionKey}' must be a region with numeric x, y, width and height");
      }
      else if (parsed.Width <= 0 || parsed.Height <= 0) {
        errors.Add($"'{CorrectRegionKey}' width and height must be more than 0");
      }
      else {
        region = parsed;
      }
    }

    int? position = null;
    if (hasPosition) {
      if (!QuizPayloadReader.TryAsInt(positionElement, out var parsedPosition)) {
        errors.Add($"'{CorrectPositionKey}' must be a whole number");
      }
      else if (parsedPosition < 1) {
        errors.Add($"'{CorrectPositionKey}' must be 1 or more");
      }
      else {
        position = parsedPosition;
      }
    }

    if (errors.Count == 0) {
      payload = new QuizLearnPayload {
        LookupStepId = lookupStepId!.Trim(),
        CorrectRegion = region,
        CorrectPosition = position
      };
    }

    return errors;
  }
}
