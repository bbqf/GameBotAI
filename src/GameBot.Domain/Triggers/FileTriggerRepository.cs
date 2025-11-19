using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace GameBot.Domain.Triggers;

public sealed class FileTriggerRepository : ITriggerRepository {
  private readonly string _dir;
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) {
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
  };

  public FileTriggerRepository(string root) {
    _dir = Path.Combine(root, "triggers");
    Directory.CreateDirectory(_dir);
  }

  public async Task<Trigger?> GetAsync(string id, CancellationToken ct = default) {
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<Trigger>(fs, JsonOpts, ct).ConfigureAwait(false);
  }

  public async Task UpsertAsync(Trigger trigger, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(trigger);
    if (string.IsNullOrWhiteSpace(trigger.Id)) throw new ArgumentException("Trigger.Id is required");
    var path = Path.Combine(_dir, trigger.Id + ".json");
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, trigger, JsonOpts, ct).ConfigureAwait(false);
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return Task.FromResult(false);
    File.Delete(path);
    return Task.FromResult(true);
  }

  public async Task<IReadOnlyList<Trigger>> ListAsync(CancellationToken ct = default) {
    var list = new List<Trigger>();
    foreach (var file in Directory.EnumerateFiles(_dir, "*.json")) {
      ct.ThrowIfCancellationRequested();
      try {
        using var fs = File.OpenRead(file);
        var trig = await JsonSerializer.DeserializeAsync<Trigger>(fs, JsonOpts, ct).ConfigureAwait(false);
        if (trig is not null) list.Add(trig);
      }
      catch { }
    }
    return list;
  }
}
