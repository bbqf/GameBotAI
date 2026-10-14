namespace GameBot.Domain.Quiz;

/// <summary>
/// One stored quiz answer (feature 142). The key is (store, game, category, question). The question and
/// the answer are normalized text (see <see cref="QuizTextNormalizer"/>).
/// </summary>
public sealed class QuizRecord {
  /// <summary>The record id: a GUID in "N" format. Unique across all stores.</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>The store name. Free text. Compared without case.</summary>
  public string Store { get; set; } = string.Empty;

  /// <summary>The game identifier. Compared exactly.</summary>
  public string Game { get; set; } = string.Empty;

  /// <summary>The category. No default. Compared without case.</summary>
  public string Category { get; set; } = string.Empty;

  /// <summary>The normalized question text.</summary>
  public string Question { get; set; } = string.Empty;

  /// <summary>The normalized correct answer text.</summary>
  public string Answer { get; set; } = string.Empty;

  /// <summary>When the record was created (UTC).</summary>
  public DateTimeOffset CreatedAt { get; set; }

  /// <summary>When the record was created or last changed (UTC).</summary>
  public DateTimeOffset UpdatedAt { get; set; }
}
