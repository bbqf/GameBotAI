using System.Text.Json;

namespace GameBot.Domain.Games;

public sealed class FileGameRepository : IGameRepository {
  private readonly string _dir;
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

  public FileGameRepository(string root) {
    _dir = Path.Combine(root, "games");
    Directory.CreateDirectory(_dir);
  }

  public async Task<GameArtifact> AddAsync(GameArtifact artifact, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(artifact);
    var id = string.IsNullOrWhiteSpace(artifact.Id) ? Guid.NewGuid().ToString("N") : artifact.Id;
    artifact.Id = id;
    var path = Path.Combine(_dir, id + ".json");
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, artifact, JsonOpts, ct).ConfigureAwait(false);
    return artifact;
  }

  public async Task<GameArtifact?> GetAsync(string id, CancellationToken ct = default) {
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<GameArtifact>(fs, JsonOpts, ct).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<GameArtifact>> ListAsync(CancellationToken ct = default) {
    var list = new List<GameArtifact>();
    foreach (var file in Directory.EnumerateFiles(_dir, "*.json")) {
      using var fs = File.OpenRead(file);
      var item = await JsonSerializer.DeserializeAsync<GameArtifact>(fs, JsonOpts, ct).ConfigureAwait(false);
      if (item is not null) list.Add(item);
    }
    return list;
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    var path = Path.Combine(_dir, id + ".json");
    if (!File.Exists(path)) return Task.FromResult(false);
    File.Delete(path);
    return Task.FromResult(true);
  }
}
