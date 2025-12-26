using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace GameBot.Domain.Triggers;

public sealed class FileTriggerRepository : ITriggerRepository {
  private readonly string _dir;
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) {
    WriteIndented = true,
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
  };
  private static readonly Regex SafeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

  public FileTriggerRepository(string root) {
    _dir = Path.Combine(root, "triggers");
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

  public async Task<Trigger?> GetAsync(string id, CancellationToken ct = default) {
    if (!TryGetSafePath(id, out var path)) return null;
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<Trigger>(fs, JsonOpts, ct).ConfigureAwait(false);
  }

  public async Task UpsertAsync(Trigger trigger, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(trigger);
    if (string.IsNullOrWhiteSpace(trigger.Id)) throw new ArgumentException("Trigger.Id is required", nameof(trigger));
    if (!TryGetSafePath(trigger.Id, out var path)) throw new ArgumentException("Invalid trigger identifier", nameof(trigger));
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, trigger, JsonOpts, ct).ConfigureAwait(false);
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    if (!TryGetSafePath(id, out var path)) return Task.FromResult(false);
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
