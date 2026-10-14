namespace GameBot.Domain.Quiz;

/// <summary>What an upsert did (feature 142, research R8).</summary>
public enum QuizUpsertKind {
  /// <summary>A new record was made.</summary>
  Created,

  /// <summary>The key exists with the same answer. Only <c>updatedAt</c> changed.</summary>
  Unchanged,

  /// <summary>The key exists with another answer. The answer was replaced.</summary>
  Replaced
}

/// <summary>The result of <see cref="IQuizAnswerRepository.UpsertAsync"/>.</summary>
/// <param name="Kind">What the upsert did.</param>
/// <param name="Record">The record after the change.</param>
/// <param name="PreviousAnswer">The answer before a replace; null for the other kinds.</param>
public sealed record QuizUpsertResult(QuizUpsertKind Kind, QuizRecord Record, string? PreviousAnswer);

/// <summary>
/// Storage of quiz answers (feature 142). A store has a name. A record key is (store, game, category,
/// question). Game is compared exactly. Store and category are compared without case.
/// </summary>
public interface IQuizAnswerRepository {
  /// <summary>
  /// Finds the record of the best matching question in one store, for one game and category. A record
  /// matches when the similarity of its question to <paramref name="question"/> is at least
  /// <paramref name="similarity"/>. On an equal best score the newest <c>updatedAt</c> wins.
  /// </summary>
  /// <exception cref="QuizStoreUnavailableException">The store file cannot be read.</exception>
  Task<QuizRecord?> FindBestAsync(string store, string game, string category, string question, double similarity, CancellationToken ct = default);

  /// <summary>
  /// Creates a record, or changes the record with the same key. The question and the answer are normalized
  /// again. A blank store, game, category, question, or answer throws <see cref="ArgumentException"/>.
  /// </summary>
  /// <exception cref="QuizStoreUnavailableException">The store file cannot be read or written.</exception>
  Task<QuizUpsertResult> UpsertAsync(string store, string game, string category, string question, string answer, CancellationToken ct = default);

  /// <summary>
  /// Lists records of all stores. A filter that is null or blank is not applied. A store file that is damaged
  /// is skipped.
  /// </summary>
  Task<IReadOnlyList<QuizRecord>> ListAsync(string? store, string? game, string? category, CancellationToken ct = default);

  /// <summary>Gets one record by id. Null when the id is not valid or no record has it.</summary>
  Task<QuizRecord?> GetAsync(string id, CancellationToken ct = default);

  /// <summary>Removes one record by id. False when the id is not valid or no record has it.</summary>
  Task<bool> DeleteAsync(string id, CancellationToken ct = default);
}
