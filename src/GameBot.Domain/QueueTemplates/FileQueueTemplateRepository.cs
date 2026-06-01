using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GameBot.Domain.QueueTemplates {
  /// <summary>
  /// File-backed repository for queue templates, stored as JSON under data/queue-templates.
  /// Persists the name and the ordered sequence entries. Mirrors the safe-id/path-traversal
  /// guard used by other file repositories in the project.
  /// </summary>
  public class FileQueueTemplateRepository : IQueueTemplateRepository {
    private readonly string _root;
    private readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions {
      WriteIndented = true
    };
    private static readonly Regex SafeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

    public FileQueueTemplateRepository(string dataRoot) {
      _root = Path.Combine(dataRoot, "queue-templates");
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

    public async Task<QueueTemplate?> GetAsync(string id) {
      if (!TryGetSafePath(id, out var path)) return null;
      if (!File.Exists(path)) return null;
      using var stream = File.OpenRead(path);
      return await JsonSerializer.DeserializeAsync<QueueTemplate>(stream, _jsonOptions).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<QueueTemplate>> ListAsync() {
      var list = new List<QueueTemplate>();
      foreach (var file in Directory.EnumerateFiles(_root, "*.json")) {
        using var stream = File.OpenRead(file);
        var template = await JsonSerializer.DeserializeAsync<QueueTemplate>(stream, _jsonOptions).ConfigureAwait(false);
        if (template != null) list.Add(template);
      }
      return list;
    }

    public async Task<QueueTemplate?> FindByNameAsync(string name) {
      if (string.IsNullOrWhiteSpace(name)) return null;
      var all = await ListAsync().ConfigureAwait(false);
      return all.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    public async Task<QueueTemplate> CreateAsync(QueueTemplate item) {
      ArgumentNullException.ThrowIfNull(item);
      Validate(item);
      Directory.CreateDirectory(_root);
      if (string.IsNullOrWhiteSpace(item.Id)) {
        item.Id = Guid.NewGuid().ToString("N");
      }
      item.CreatedAt ??= DateTimeOffset.UtcNow;
      item.UpdatedAt = item.CreatedAt;
      await WriteAsync(item).ConfigureAwait(false);
      return item;
    }

    public async Task<QueueTemplate> UpdateAsync(QueueTemplate item) {
      ArgumentNullException.ThrowIfNull(item);
      Validate(item);
      if (string.IsNullOrWhiteSpace(item.Id)) {
        throw new InvalidOperationException("Template Id is required for update");
      }
      item.UpdatedAt = DateTimeOffset.UtcNow;
      await WriteAsync(item).ConfigureAwait(false);
      return item;
    }

    public Task<bool> DeleteAsync(string id) {
      if (!TryGetSafePath(id, out var path)) return Task.FromResult(false);
      if (!File.Exists(path)) return Task.FromResult(false);
      File.Delete(path);
      return Task.FromResult(true);
    }

    private async Task WriteAsync(QueueTemplate template) {
      if (!TryGetSafePath(template.Id, out var path)) {
        throw new InvalidOperationException("Generated or provided template ID is invalid for file storage.");
      }
      using var stream = File.Create(path);
      await JsonSerializer.SerializeAsync(stream, template, _jsonOptions).ConfigureAwait(false);
    }

    private static void Validate(QueueTemplate template) {
      if (string.IsNullOrWhiteSpace(template.Name)) {
        throw new InvalidOperationException("Template name is required.");
      }
    }
  }
}
