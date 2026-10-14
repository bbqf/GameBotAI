using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GameBot.Domain.Quiz;

/// <summary>
/// File-backed <see cref="IQuizAnswerRepository"/> (feature 142). It keeps one JSON file for each store under
/// <c>&lt;dataRoot&gt;/quiz/</c>. The file name is the first 32 hex characters of the SHA-256 hash of the
/// lower-case store name, so a store name can never act as a path. The file holds the real name. One gate for
/// each store serializes the access. A write goes to a temporary file first and then replaces the file, so a
/// crash does not leave a damaged file. A file that cannot be parsed makes its store unavailable. Other stores
/// keep working.
/// </summary>
public sealed partial class FileQuizAnswerRepository : IQuizAnswerRepository {
  /// <summary>The name of the folder under the data root.</summary>
  public const string FolderName = "quiz";

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
  };

  private readonly string _folder;
  private readonly ILogger _logger;
  private readonly TimeProvider _time;
  private readonly ConcurrentDictionary<string, SemaphoreSlim> _gates = new(StringComparer.Ordinal);

  /// <summary>Creates the repository on <paramref name="dataRoot"/>.</summary>
  public FileQuizAnswerRepository(string dataRoot, ILogger<FileQuizAnswerRepository>? logger = null, TimeProvider? timeProvider = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _folder = Path.Combine(dataRoot, FolderName);
    _logger = (ILogger?)logger ?? NullLogger.Instance;
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <summary>The folder that holds the store files.</summary>
  public string FolderPath => _folder;

  /// <inheritdoc />
  public async Task<QuizRecord?> FindBestAsync(string store, string game, string category, string question, double similarity, CancellationToken ct = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(store);
    if (string.IsNullOrWhiteSpace(game) || string.IsNullOrWhiteSpace(category)) {
      return null;
    }

    var normalized = QuizTextNormalizer.Normalize(question);
    if (normalized.Length == 0) {
      return null;
    }

    var key = StoreKey(store);
    var gate = GateFor(key);
    await gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var doc = await LoadAsync(key, ct).ConfigureAwait(false);
      QuizRecord? best = null;
      var bestScore = double.NegativeInfinity;
      foreach (var record in doc.Records) {
        if (!MatchesScope(record, game, category)) {
          continue;
        }

        var score = QuizSimilarity.Score(normalized, record.Question, similarity);
        if (score < similarity) {
          continue;
        }

        if (best is null || score > bestScore || (score == bestScore && record.UpdatedAt > best.UpdatedAt)) {
          best = record;
          bestScore = score;
        }
      }

      return best;
    }
    finally {
      gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<QuizUpsertResult> UpsertAsync(string store, string game, string category, string question, string answer, CancellationToken ct = default) {
    ArgumentException.ThrowIfNullOrWhiteSpace(store);
    ArgumentException.ThrowIfNullOrWhiteSpace(game);
    ArgumentException.ThrowIfNullOrWhiteSpace(category);
    var normalizedQuestion = QuizTextNormalizer.Normalize(question);
    var normalizedAnswer = QuizTextNormalizer.Normalize(answer);
    if (normalizedQuestion.Length == 0) {
      throw new ArgumentException("The question must not be empty.", nameof(question));
    }

    if (normalizedAnswer.Length == 0) {
      throw new ArgumentException("The answer must not be empty.", nameof(answer));
    }

    var key = StoreKey(store);
    var gate = GateFor(key);
    await gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var doc = await LoadAsync(key, ct).ConfigureAwait(false);
      var now = _time.GetUtcNow();
      var existing = doc.Records.FirstOrDefault(r =>
        MatchesScope(r, game, category) && string.Equals(r.Question, normalizedQuestion, StringComparison.Ordinal));

      QuizUpsertResult result;
      if (existing is null) {
        if (string.IsNullOrWhiteSpace(doc.Name)) {
          doc.Name = store.Trim();
        }

        var created = new QuizRecord {
          Id = Guid.NewGuid().ToString("N"),
          Store = doc.Name,
          Game = game,
          Category = category.Trim(),
          Question = normalizedQuestion,
          Answer = normalizedAnswer,
          CreatedAt = now,
          UpdatedAt = now
        };
        doc.Records.Add(created);
        result = new QuizUpsertResult(QuizUpsertKind.Created, created, null);
      }
      else if (string.Equals(existing.Answer, normalizedAnswer, StringComparison.Ordinal)) {
        existing.UpdatedAt = now;
        result = new QuizUpsertResult(QuizUpsertKind.Unchanged, existing, null);
      }
      else {
        var previous = existing.Answer;
        existing.Answer = normalizedAnswer;
        existing.UpdatedAt = now;
        result = new QuizUpsertResult(QuizUpsertKind.Replaced, existing, previous);
      }

      await WriteAsync(key, doc, ct).ConfigureAwait(false);
      return result;
    }
    finally {
      gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyList<QuizRecord>> ListAsync(string? store, string? game, string? category, CancellationToken ct = default) {
    var keys = string.IsNullOrWhiteSpace(store) ? ListStoreKeys() : new List<string> { StoreKey(store) };
    var result = new List<QuizRecord>();
    foreach (var key in keys) {
      var doc = await TryLoadLockedAsync(key, ct).ConfigureAwait(false);
      if (doc is null) {
        continue;
      }

      foreach (var record in doc.Records) {
        if (!string.IsNullOrWhiteSpace(game) && !string.Equals(record.Game, game, StringComparison.Ordinal)) {
          continue;
        }

        if (!string.IsNullOrWhiteSpace(category) && !string.Equals(record.Category, category, StringComparison.OrdinalIgnoreCase)) {
          continue;
        }

        result.Add(record);
      }
    }

    return result
      .OrderBy(r => r.Store, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.Game, StringComparer.Ordinal)
      .ThenBy(r => r.Category, StringComparer.OrdinalIgnoreCase)
      .ThenBy(r => r.Question, StringComparer.Ordinal)
      .ToList();
  }

  /// <inheritdoc />
  public async Task<QuizRecord?> GetAsync(string id, CancellationToken ct = default) {
    if (!IsValidId(id)) {
      return null;
    }

    foreach (var key in ListStoreKeys()) {
      var doc = await TryLoadLockedAsync(key, ct).ConfigureAwait(false);
      var found = doc?.Records.FirstOrDefault(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
      if (found is not null) {
        return found;
      }
    }

    return null;
  }

  /// <inheritdoc />
  public async Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    if (!IsValidId(id)) {
      return false;
    }

    foreach (var key in ListStoreKeys()) {
      var gate = GateFor(key);
      await gate.WaitAsync(ct).ConfigureAwait(false);
      try {
        StoreDocument doc;
        try {
          doc = await LoadAsync(key, ct).ConfigureAwait(false);
        }
        catch (QuizStoreUnavailableException ex) {
          Log.StoreSkipped(_logger, key, ex);
          continue;
        }

        var removed = doc.Records.RemoveAll(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed == 0) {
          continue;
        }

        await WriteAsync(key, doc, ct).ConfigureAwait(false);
        return true;
      }
      finally {
        gate.Release();
      }
    }

    return false;
  }

  /// <summary>The file key of a store name: 32 hex characters of the SHA-256 of the lower-case name.</summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Globalization", "CA1308:Normalize strings to uppercase",
    Justification = "The file key is a hash of the lower-case store name, as the data model says. It is never shown as text.")]
  internal static string StoreKey(string store) {
    var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(store.Trim().ToLowerInvariant()));
    return Convert.ToHexStringLower(bytes)[..32];
  }

  private static bool IsValidId(string? id) =>
    !string.IsNullOrWhiteSpace(id) && Guid.TryParseExact(id, "N", out _);

  private static bool MatchesScope(QuizRecord record, string game, string category) =>
    string.Equals(record.Game, game, StringComparison.Ordinal)
    && string.Equals(record.Category, category.Trim(), StringComparison.OrdinalIgnoreCase);

  private SemaphoreSlim GateFor(string key) => _gates.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));

  private string PathFor(string key) => Path.Combine(_folder, key + ".json");

  private List<string> ListStoreKeys() {
    if (!Directory.Exists(_folder)) {
      return new List<string>();
    }

    return Directory.EnumerateFiles(_folder, "*.json")
      .Where(path => string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase))
      .Select(path => Path.GetFileNameWithoutExtension(path))
      .OrderBy(name => name, StringComparer.Ordinal)
      .ToList();
  }

  // Loads a store under its gate. A damaged store gives null and a warning.
  private async Task<StoreDocument?> TryLoadLockedAsync(string key, CancellationToken ct) {
    var gate = GateFor(key);
    await gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      return await LoadAsync(key, ct).ConfigureAwait(false);
    }
    catch (QuizStoreUnavailableException ex) {
      Log.StoreSkipped(_logger, key, ex);
      return null;
    }
    finally {
      gate.Release();
    }
  }

  // Call only while the gate of the store is held.
  private async Task<StoreDocument> LoadAsync(string key, CancellationToken ct) {
    var path = PathFor(key);
    if (!File.Exists(path)) {
      return new StoreDocument();
    }

    try {
      var text = await File.ReadAllTextAsync(path, Encoding.UTF8, ct).ConfigureAwait(false);
      var doc = JsonSerializer.Deserialize<StoreDocument>(text, JsonOptions)
        ?? throw new QuizStoreUnavailableException("The quiz store file is empty or damaged.");
      doc.Records ??= new List<QuizRecord>();
      return doc;
    }
    catch (JsonException ex) {
      throw new QuizStoreUnavailableException("The quiz store file is damaged.", ex);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      throw new QuizStoreUnavailableException("The quiz store file cannot be read.", ex);
    }
  }

  // Call only while the gate of the store is held.
  private async Task WriteAsync(string key, StoreDocument doc, CancellationToken ct) {
    var path = PathFor(key);
    var temp = Path.Combine(_folder, key + "." + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture) + ".tmp");
    try {
      Directory.CreateDirectory(_folder);
      await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(doc, JsonOptions), new UTF8Encoding(false), ct).ConfigureAwait(false);
      File.Move(temp, path, overwrite: true);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      TryDelete(temp);
      throw new QuizStoreUnavailableException("The quiz store file cannot be written.", ex);
    }
  }

  private static void TryDelete(string path) {
    try {
      File.Delete(path);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // The temporary file stays. The next write uses a new name.
    }
  }

  private sealed class StoreDocument {
    public string Name { get; set; } = string.Empty;
    public List<QuizRecord> Records { get; set; } = new();
  }

  private static partial class Log {
    [LoggerMessage(EventId = 14201, Level = LogLevel.Warning, Message = "The quiz store file {Key} is damaged or cannot be read. The store is skipped.")]
    public static partial void StoreSkipped(ILogger logger, string key, Exception exception);
  }
}
