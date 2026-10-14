namespace GameBot.Domain.Quiz;

/// <summary>
/// What a finished <c>quiz-lookup</c> step keeps for a later <c>quiz-learn</c> step of the same run
/// (feature 142). Held in memory only. A lookup keeps a result only when its outcome is <c>pick_N</c>.
/// </summary>
public sealed class QuizLookupResult {
  /// <summary>The id of the lookup step.</summary>
  public string StepId { get; init; } = string.Empty;

  /// <summary>The store name that the lookup used.</summary>
  public string Store { get; init; } = string.Empty;

  /// <summary>The game identifier that the lookup used.</summary>
  public string Game { get; init; } = string.Empty;

  /// <summary>The category that the lookup used.</summary>
  public string Category { get; init; } = string.Empty;

  /// <summary>The normalized question text.</summary>
  public string Question { get; init; } = string.Empty;

  /// <summary>The normalized answer texts, in region order.</summary>
  public IReadOnlyList<string> Answers { get; init; } = Array.Empty<string>();

  /// <summary>The similarity limit that the lookup used.</summary>
  public double Similarity { get; init; }

  /// <summary>The outcome of the lookup.</summary>
  public string Outcome { get; init; } = string.Empty;
}
