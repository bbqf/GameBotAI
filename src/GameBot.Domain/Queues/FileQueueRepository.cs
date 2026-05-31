using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GameBot.Domain.Queues {
  /// <summary>
  /// File-backed repository for queue configuration, stored as JSON under data/queues.
  /// Persists configuration only — entries and status are runtime concerns and are
  /// never written here. Mirrors the safe-id/path-traversal guard used by other
  /// file repositories in the project.
  /// </summary>
  public class FileQueueRepository : IQueueRepository {
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
      WriteIndented = true
    };
    private static readonly Regex SafeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    public FileQueueRepository(string dataRoot) {
      _root = Path.Combine(dataRoot, "queues");
      Directory.CreateDirectory(_root);
    }

    private bool TryGetSafePath(string id, [NotNullWhen(true)] out string? path) {
      path = null;
      if (string.IsNullOrWhiteSpace(id)) return false;
      if (!SafeIdPattern.IsMatch(id)) return false;

      var baseDirFull = Path.GetFullPath(_root);
      var candidate = Path.Combine(baseDirFull, id + ".json");
      var candidateFull = Path.GetFullPath(candidate);

      if (!candidateFull.StartsWith(baseDirFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
          && !string.Equals(candidateFull, baseDirFull, StringComparison.Ordinal)) {
        return false;
      }

      path = candidateFull;
      return true;
    }

    public async Task<ExecutionQueue?> GetAsync(string id) {
      if (!TryGetSafePath(id, out var path)) return null;
      if (!File.Exists(path)) return null;
      using var stream = File.OpenRead(path);
      return await JsonSerializer.DeserializeAsync<ExecutionQueue>(stream, _jsonOptions).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ExecutionQueue>> ListAsync() {
      var list = new List<ExecutionQueue>();
      foreach (var file in Directory.EnumerateFiles(_root, "*.json")) {
        using var stream = File.OpenRead(file);
        var queue = await JsonSerializer.DeserializeAsync<ExecutionQueue>(stream, _jsonOptions).ConfigureAwait(false);
        if (queue != null) list.Add(queue);
      }
      return list;
    }

    public async Task<ExecutionQueue> CreateAsync(ExecutionQueue queue) {
      ArgumentNullException.ThrowIfNull(queue);
      Validate(queue);
      Directory.CreateDirectory(_root);
      if (string.IsNullOrWhiteSpace(queue.Id)) {
        queue.Id = Guid.NewGuid().ToString("N");
      }
      queue.CreatedAt ??= DateTimeOffset.UtcNow;
      queue.UpdatedAt = queue.CreatedAt;
      if (!TryGetSafePath(queue.Id, out var path)) {
        throw new InvalidOperationException("Generated or provided queue ID is invalid for file storage.");
      }
      using var stream = File.Create(path);
      await JsonSerializer.SerializeAsync(stream, queue, _jsonOptions).ConfigureAwait(false);
      return queue;
    }

    public async Task<ExecutionQueue> UpdateAsync(ExecutionQueue queue) {
      ArgumentNullException.ThrowIfNull(queue);
      Validate(queue);
      if (string.IsNullOrWhiteSpace(queue.Id)) {
        throw new InvalidOperationException("Queue Id is required for update");
      }
      if (!TryGetSafePath(queue.Id, out var path)) {
        throw new InvalidOperationException("Invalid queue identifier");
      }
      queue.UpdatedAt = DateTimeOffset.UtcNow;
      using var stream = File.Create(path);
      await JsonSerializer.SerializeAsync(stream, queue, _jsonOptions).ConfigureAwait(false);
      return queue;
    }

    public Task<bool> DeleteAsync(string id) {
      if (!TryGetSafePath(id, out var path)) return Task.FromResult(false);
      if (!File.Exists(path)) return Task.FromResult(false);
      File.Delete(path);
      return Task.FromResult(true);
    }

    private static void Validate(ExecutionQueue queue) {
      if (string.IsNullOrWhiteSpace(queue.Name)) {
        throw new InvalidOperationException("Queue name is required.");
      }
      if (string.IsNullOrWhiteSpace(queue.EmulatorSerial)) {
        throw new InvalidOperationException("Queue emulatorSerial is required.");
      }
    }
  }
}
