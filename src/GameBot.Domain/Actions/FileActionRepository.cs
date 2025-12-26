using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace GameBot.Domain.Actions;

public sealed class FileActionRepository : IActionRepository {
  private readonly string _dir;

  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) {
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
  };

  // Allow only simple, single-component identifiers (letters, digits, underscore, hyphen).
  private static readonly Regex SafeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

  public FileActionRepository(string root) {
    _dir = Path.Combine(root, "actions");
    Directory.CreateDirectory(_dir);
  }

  private bool TryGetSafePath(string id, [NotNullWhen(true)] out string? path) {
    path = null;
    if (string.IsNullOrWhiteSpace(id)) return false;
    if (!SafeIdPattern.IsMatch(id)) return false;

    var baseDirFull = Path.GetFullPath(_dir);
    var candidate = Path.Combine(baseDirFull, id + ".json");
    var candidateFull = Path.GetFullPath(candidate);

    if (!candidateFull.StartsWith(baseDirFull + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        && !string.Equals(candidateFull, baseDirFull, StringComparison.Ordinal)) {
      return false;
    }

    path = candidateFull;
    return true;
  }

  public async Task<Action> AddAsync(Action action, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(action);
    var id = string.IsNullOrWhiteSpace(action.Id) ? Guid.NewGuid().ToString("N") : action.Id;
    action.Id = id;
    if (!TryGetSafePath(id, out var path)) {
      throw new InvalidOperationException("Generated or provided action ID is invalid for file storage.");
    }
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, action, JsonOpts, ct).ConfigureAwait(false);
    return action;
  }

  public async Task<Action?> GetAsync(string id, CancellationToken ct = default) {
    if (!TryGetSafePath(id, out var path)) return null;
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    var entity = await JsonSerializer.DeserializeAsync<Action>(fs, JsonOpts, ct).ConfigureAwait(false);
    entity ??= new Action { Id = id, Name = id, GameId = string.Empty };
    return entity;
  }

  public async Task<IReadOnlyList<Action>> ListAsync(string? gameId = null, CancellationToken ct = default) {
    var list = new List<Action>();
    foreach (var file in Directory.EnumerateFiles(_dir, "*.json")) {
      using var fs = File.OpenRead(file);
      var item = await JsonSerializer.DeserializeAsync<Action>(fs, JsonOpts, ct).ConfigureAwait(false);
      if (item is not null && (gameId is null || string.Equals(item.GameId, gameId, StringComparison.OrdinalIgnoreCase)))
        list.Add(item);
    }
    return list;
  }

  public async Task<Action?> UpdateAsync(Action action, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(action);
    if (string.IsNullOrWhiteSpace(action.Id)) return null;
    if (!TryGetSafePath(action.Id, out var path)) return null;
    if (!File.Exists(path)) return null;
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, action, JsonOpts, ct).ConfigureAwait(false);
    return action;
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    if (!TryGetSafePath(id, out var path)) return Task.FromResult(false);
    if (!File.Exists(path)) return Task.FromResult(false);
    File.Delete(path);
    return Task.FromResult(true);
  }
}
