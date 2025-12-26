using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GameBot.Domain.Actions;

public sealed class FileActionRepository : IActionRepository {
  private readonly string _dir;

  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) {
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
  };

  public FileActionRepository(string root) {
    _dir = Path.Combine(root, "actions");
    Directory.CreateDirectory(_dir);
  }

  public async Task<Action> AddAsync(Action action, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(action);
    var id = string.IsNullOrWhiteSpace(action.Id) ? Guid.NewGuid().ToString("N") : action.Id;
    action.Id = id;
    var path = Path.Combine(_dir, id + ".json");
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, action, JsonOpts, ct).ConfigureAwait(false);
    return action;
  }

  public async Task<Action?> GetAsync(string id, CancellationToken ct = default) {
    var path = Path.Combine(_dir, id + ".json");
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
    var path = Path.Combine(_dir, action.Id + ".json");
    if (!File.Exists(path)) return null;
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, action, JsonOpts, ct).ConfigureAwait(false);
    return action;
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return Task.FromResult(false);
    File.Delete(path);
    return Task.FromResult(true);
  }
}
