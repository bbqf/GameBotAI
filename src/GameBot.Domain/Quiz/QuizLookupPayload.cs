using System.Text.Json;
using GameBot.Domain.Commands;
using GameBot.Domain.Commands.SelfReschedule;

namespace GameBot.Domain.Quiz;

/// <summary>
/// The typed view of a <c>quiz-lookup</c> action (feature 142). The dictionary stays the storage. This reader
/// gives the values, and <see cref="Validate"/> gives the save-time errors.
/// </summary>
public sealed class QuizLookupPayload {
  /// <summary>The wire key of the game identifier.</summary>
  public const string GameKey = "game";

  /// <summary>The wire key of the category.</summary>
  public const string CategoryKey = "category";

  /// <summary>The wire key of the store name.</summary>
  public const string StoreKey = "store";

  /// <summary>The wire key of the question region.</summary>
  public const string QuestionKey = "question";

  /// <summary>The wire key of the answer regions.</summary>
  public const string AnswersKey = "answers";

  /// <summary>The wire key of the similarity limit.</summary>
  public const string SimilarityKey = "similarity";

  /// <summary>The similarity limit when the step has none.</summary>
  public const double DefaultSimilarity = 0.85;

  /// <summary>The fewest answer regions.</summary>
  public const int MinAnswers = 2;

  /// <summary>The game identifier. Compared exactly.</summary>
  public string Game { get; private set; } = string.Empty;

  /// <summary>The category. Compared without case.</summary>
  public string Category { get; private set; } = string.Empty;

  /// <summary>The store name. Compared without case.</summary>
  public string Store { get; private set; } = string.Empty;

  /// <summary>The region of the question text.</summary>
  public OcrOffsetRegion Question { get; private set; }

  /// <summary>The regions of the answer texts, in order. Position 1 is the first region.</summary>
  public IReadOnlyList<OcrOffsetRegion> Answers { get; private set; } = Array.Empty<OcrOffsetRegion>();

  /// <summary>The similarity limit, from 0 to 1.</summary>
  public double Similarity { get; private set; } = DefaultSimilarity;

  /// <summary>Reads the payload. False with the first error when the payload is not valid.</summary>
  public static bool TryRead(SequenceActionPayload? action, out QuizLookupPayload? payload, out string? error) {
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

  private static List<string> Parse(SequenceActionPayload? action, out QuizLookupPayload? payload) {
    var errors = new List<string>();
    payload = null;
    if (action is null) {
      errors.Add("action payload is missing");
      return errors;
    }

    var game = RequiredText(action, GameKey, errors);
    var category = RequiredText(action, CategoryKey, errors);
    var store = RequiredText(action, StoreKey, errors);

    OcrOffsetRegion question = default;
    if (!QuizPayloadReader.TryGet(action, QuestionKey, out var questionElement)) {
      errors.Add("'question' region is required");
    }
    else if (!QuizPayloadReader.TryReadRegion(questionElement, out question)) {
      errors.Add("'question' must be a region with numeric x, y, width and height");
    }
    else if (question.Width <= 0 || question.Height <= 0) {
      errors.Add("'question' region width and height must be more than 0");
    }

    var answers = ReadAnswers(action, errors);

    var similarity = DefaultSimilarity;
    if (QuizPayloadReader.TryGet(action, SimilarityKey, out var similarityElement)) {
      if (!QuizPayloadReader.TryAsDouble(similarityElement, out similarity)
          || double.IsNaN(similarity) || similarity < 0 || similarity > 1) {
        errors.Add("'similarity' must be a number from 0 to 1");
        similarity = DefaultSimilarity;
      }
    }

    if (errors.Count == 0) {
      payload = new QuizLookupPayload {
        Game = game!,
        Category = category!,
        Store = store!,
        Question = question,
        Answers = answers,
        Similarity = similarity
      };
    }

    return errors;
  }

  private static string? RequiredText(SequenceActionPayload action, string key, List<string> errors) {
    var value = QuizPayloadReader.ReadString(action, key);
    if (string.IsNullOrWhiteSpace(value)) {
      errors.Add($"'{key}' is required and must not be blank");
      return null;
    }

    return value.Trim();
  }

  private static List<OcrOffsetRegion> ReadAnswers(SequenceActionPayload action, List<string> errors) {
    var answers = new List<OcrOffsetRegion>();
    if (!QuizPayloadReader.TryGet(action, AnswersKey, out var element) || element.ValueKind != JsonValueKind.Array) {
      errors.Add($"'answers' must be a list of {MinAnswers} or more regions");
      return answers;
    }

    var index = 0;
    foreach (var item in element.EnumerateArray()) {
      index++;
      if (!QuizPayloadReader.TryReadRegion(item, out var region)) {
        errors.Add($"'answers' item {index} must be a region with numeric x, y, width and height");
      }
      else if (region.Width <= 0 || region.Height <= 0) {
        errors.Add($"'answers' item {index} width and height must be more than 0");
      }
      else {
        answers.Add(region);
      }
    }

    if (index < MinAnswers) {
      errors.Add($"'answers' must have {MinAnswers} or more regions");
    }

    return answers;
  }
}
