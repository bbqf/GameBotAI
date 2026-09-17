using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// File-backed <see cref="IQueueRunStateStore"/>: one JSON document, <c>queue-run-state.json</c>, at the
  /// data root. Deliberately not under <c>queues/</c>, whose every <c>*.json</c> file the queue
  /// repository reads as a queue.
  /// <para>
  /// Writes are serialized in-process and land atomically (temp file, then replace), so a crash
  /// mid-write cannot leave a half-written record. A corrupt record is still survivable: the one read
  /// that matters — the startup resume pass — gets an <see cref="InvalidDataException"/> it can log,
  /// while starts and run teardowns start from an empty record and overwrite it, so a single bad file
  /// never breaks a queue start or disables resume for good.
  /// </para>
  /// </summary>
  public sealed class FileQueueRunStateStore : IQueueRunStateStore, IDisposable {
    private const string FileName = "queue-run-state.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public FileQueueRunStateStore(string dataRoot) {
      Directory.CreateDirectory(dataRoot);
      _path = Path.Combine(dataRoot, FileName);
    }

    public async Task MarkRunningAsync(string queueId) {
      ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
      await _gate.WaitAsync().ConfigureAwait(false);
      try {
        var ids = await ReadOrEmptyAsync().ConfigureAwait(false);
        if (ids.Contains(queueId, StringComparer.Ordinal)) return;
        ids.Add(queueId);
        await WriteAsync(ids).ConfigureAwait(false);
      }
      finally {
        _gate.Release();
      }
    }

    public async Task ClearAsync(string queueId) {
      ArgumentException.ThrowIfNullOrWhiteSpace(queueId);
      await _gate.WaitAsync().ConfigureAwait(false);
      try {
        var ids = await ReadOrEmptyAsync().ConfigureAwait(false);
        if (ids.RemoveAll(id => string.Equals(id, queueId, StringComparison.Ordinal)) == 0) return;
        await WriteAsync(ids).ConfigureAwait(false);
      }
      finally {
        _gate.Release();
      }
    }

    public async Task<IReadOnlyList<string>> ListRunningAsync() {
      await _gate.WaitAsync().ConfigureAwait(false);
      try {
        return await ReadAsync().ConfigureAwait(false);
      }
      finally {
        _gate.Release();
      }
    }

    public void Dispose() => _gate.Dispose();

    private async Task<List<string>> ReadAsync() {
      if (!File.Exists(_path)) return new List<string>();
      var text = await File.ReadAllTextAsync(_path).ConfigureAwait(false);
      if (string.IsNullOrWhiteSpace(text)) return new List<string>();
      try {
        var doc = JsonSerializer.Deserialize<RunStateDocument>(text, JsonOptions);
        return doc?.RunningQueueIds?
          .Where(id => !string.IsNullOrWhiteSpace(id))
          .Distinct(StringComparer.Ordinal)
          .ToList() ?? new List<string>();
      }
      catch (JsonException ex) {
        throw new InvalidDataException($"The running-queue record '{_path}' is corrupt.", ex);
      }
    }

    private async Task<List<string>> ReadOrEmptyAsync() {
      try {
        return await ReadAsync().ConfigureAwait(false);
      }
      catch (InvalidDataException) {
        // Overwritten by the write that follows, which heals the record.
        return new List<string>();
      }
    }

    private async Task WriteAsync(List<string> ids) {
      var temp = _path + ".tmp";
      await File.WriteAllTextAsync(temp, JsonSerializer.Serialize(new RunStateDocument { RunningQueueIds = ids }, JsonOptions)).ConfigureAwait(false);
      File.Move(temp, _path, overwrite: true);
    }

    private sealed class RunStateDocument {
      [JsonPropertyName("runningQueueIds")]
      public List<string>? RunningQueueIds { get; set; }
    }
  }
}
