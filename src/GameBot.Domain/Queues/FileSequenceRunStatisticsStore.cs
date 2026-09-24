using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace GameBot.Domain.Queues;

/// <summary>
/// File-backed <see cref="ISequenceRunStatisticsStore"/> (feature 105). It keeps one JSON file for each
/// queue at <c>&lt;dataRoot&gt;/queue-sequence-stats/&lt;queueId&gt;.json</c>. The folder is not under
/// <c>queues/</c>, because the queue repository reads each <c>*.json</c> file there as a queue.
/// <para>
/// The store keeps an in-memory copy of each queue file that it read, so a queue read and a
/// <c>lastRun</c> condition do not read the disk again. One gate serializes all access. Each write goes
/// to a temporary file first and then replaces the file, so a crash cannot leave a half-written file.
/// </para>
/// <para>
/// A damaged file never causes an exception on a read: the store logs a warning and uses empty
/// statistics for that queue. The next record for the queue overwrites the file.
/// </para>
/// </summary>
public sealed partial class FileSequenceRunStatisticsStore : ISequenceRunStatisticsStore, IDisposable {
  /// <summary>The name of the folder under the data root.</summary>
  public const string FolderName = "queue-sequence-stats";

  /// <summary>The schema version that this store writes and reads.</summary>
  public const int CurrentSchemaVersion = 1;

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
  };

  private readonly string _folder;
  private readonly ILogger _logger;
  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly Dictionary<string, Dictionary<string, SequenceRunStatistics>> _cache = new(StringComparer.Ordinal);

  // Queue IDs are never used again after a delete. A record for a deleted queue can only come from a run
  // that ended after the delete, so the store ignores it and does not make an orphan file.
  private readonly HashSet<string> _deleted = new(StringComparer.Ordinal);

  /// <summary>Creates the store on <paramref name="dataRoot"/>.</summary>
  public FileSequenceRunStatisticsStore(string dataRoot, ILogger<FileSequenceRunStatisticsStore>? logger = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _folder = Path.Combine(dataRoot, FolderName);
    Directory.CreateDirectory(_folder);
    _logger = (ILogger?)logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
  }

  /// <summary>The folder that holds the statistics files.</summary>
  public string FolderPath => _folder;

  /// <inheritdoc />
  public async Task RecordAsync(string queueId, string sequenceId, SequenceRunRecord record, CancellationToken ct = default) {
    EnsureSafeQueueId(queueId);
    ArgumentException.ThrowIfNullOrWhiteSpace(sequenceId);
    ArgumentNullException.ThrowIfNull(record);

    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      if (_deleted.Contains(queueId)) return;
      var sequences = await LoadAsync(queueId).ConfigureAwait(false);
      if (!sequences.TryGetValue(sequenceId, out var stats)) {
        stats = new SequenceRunStatistics();
        sequences[sequenceId] = stats;
      }

      stats.Apply(record);
      await WriteAsync(queueId, sequences).ConfigureAwait(false);
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<IReadOnlyDictionary<string, SequenceRunStatistics>> GetForQueueAsync(string queueId, CancellationToken ct = default) {
    EnsureSafeQueueId(queueId);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var sequences = await LoadAsync(queueId).ConfigureAwait(false);
      return sequences.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal);
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task<SequenceRunStatistics?> GetAsync(string queueId, string sequenceId, CancellationToken ct = default) {
    EnsureSafeQueueId(queueId);
    if (string.IsNullOrEmpty(sequenceId)) return null;
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      var sequences = await LoadAsync(queueId).ConfigureAwait(false);
      return sequences.TryGetValue(sequenceId, out var stats) ? stats.Clone() : null;
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public async Task DeleteQueueAsync(string queueId, CancellationToken ct = default) {
    EnsureSafeQueueId(queueId);
    await _gate.WaitAsync(ct).ConfigureAwait(false);
    try {
      _cache.Remove(queueId);
      _deleted.Add(queueId);
      var path = PathFor(queueId);
      if (File.Exists(path)) File.Delete(path);
      if (File.Exists(path + ".tmp")) File.Delete(path + ".tmp");
    }
    finally {
      _gate.Release();
    }
  }

  /// <inheritdoc />
  public void Dispose() => _gate.Dispose();

  private string PathFor(string queueId) => Path.Combine(_folder, queueId + ".json");

  /// <summary>
  /// Queue IDs come from the queue repository, so a bad ID is a defect in the code. The check stops a
  /// path outside the statistics folder.
  /// </summary>
  private static void EnsureSafeQueueId(string queueId) {
    ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
    if (queueId.Contains("..", StringComparison.Ordinal)
        || queueId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
        || queueId.Contains('/', StringComparison.Ordinal)
        || queueId.Contains('\\', StringComparison.Ordinal)) {
      throw new ArgumentException($"Queue id '{queueId}' is not a safe file name.", nameof(queueId));
    }
  }

  // Call only while the gate is held.
  private async Task<Dictionary<string, SequenceRunStatistics>> LoadAsync(string queueId) {
    if (_cache.TryGetValue(queueId, out var cached)) return cached;

    var loaded = await ReadFileAsync(queueId).ConfigureAwait(false);
    _cache[queueId] = loaded;
    return loaded;
  }

  private async Task<Dictionary<string, SequenceRunStatistics>> ReadFileAsync(string queueId) {
    var path = PathFor(queueId);
    if (!File.Exists(path)) return NewMap();

    try {
      var text = await File.ReadAllTextAsync(path).ConfigureAwait(false);
      var doc = JsonSerializer.Deserialize<StatisticsDocument>(text, JsonOptions);
      if (doc is null || doc.SchemaVersion is < 1 or > CurrentSchemaVersion) {
        Log.DamagedFile(_logger, queueId, path, null);
        return NewMap();
      }

      var map = NewMap();
      foreach (var (sequenceId, stats) in doc.Sequences ?? new Dictionary<string, SequenceRunStatistics>()) {
        if (string.IsNullOrEmpty(sequenceId) || stats is null) continue;
        map[sequenceId] = stats;
      }

      return map;
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) {
      Log.DamagedFile(_logger, queueId, path, ex);
      return NewMap();
    }
  }

  private async Task WriteAsync(string queueId, Dictionary<string, SequenceRunStatistics> sequences) {
    var path = PathFor(queueId);
    var temp = path + ".tmp";
    var doc = new StatisticsDocument {
      SchemaVersion = CurrentSchemaVersion,
      QueueId = queueId,
      Sequences = new SortedDictionary<string, SequenceRunStatistics>(sequences, StringComparer.Ordinal)
    };
    await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(doc, JsonOptions)).ConfigureAwait(false);
    File.Move(temp, path, overwrite: true);
  }

  private static Dictionary<string, SequenceRunStatistics> NewMap() => new(StringComparer.Ordinal);

  private sealed class StatisticsDocument {
    public int SchemaVersion { get; set; }
    public string? QueueId { get; set; }
    public IDictionary<string, SequenceRunStatistics>? Sequences { get; set; }
  }

  private static partial class Log {
    [LoggerMessage(EventId = 10501, Level = LogLevel.Warning, Message = "The run statistics file of queue {QueueId} at {Path} is damaged or has an unknown schema version. The queue uses empty statistics until the next run record.")]
    public static partial void DamagedFile(ILogger logger, string queueId, string path, Exception? exception);
  }
}
