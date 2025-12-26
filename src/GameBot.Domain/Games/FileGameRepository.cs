using System.Text.Json;

namespace GameBot.Domain.Games;

public sealed class FileGameRepository : IGameRepository {
  private readonly string _dir;
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

  public FileGameRepository(string root) {
    _dir = Path.Combine(root, "games");
    Directory.CreateDirectory(_dir);
  }

  private string GetPathForId(string id) {
    ArgumentException.ThrowIfNullOrWhiteSpace(id);

    // Disallow directory traversal and path separators in IDs.
    if (id.Contains("..", StringComparison.Ordinal) ||
        id.Contains(Path.DirectorySeparatorChar) ||
        id.Contains(Path.AltDirectorySeparatorChar)) {
      throw new ArgumentException("Invalid game identifier.", nameof(id));
    }

    var fileName = id + ".json";
    var baseDir = Path.GetFullPath(_dir);
    var fullPath = Path.GetFullPath(Path.Combine(baseDir, fileName));

    if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.Ordinal)) {
      throw new ArgumentException("Invalid game identifier.", nameof(id));
    }

    return fullPath;
  }

  public async Task<GameArtifact> AddAsync(GameArtifact artifact, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(artifact);
    var id = string.IsNullOrWhiteSpace(artifact.Id) ? Guid.NewGuid().ToString("N") : artifact.Id;
    artifact.Id = id;
    var path = GetPathForId(id);
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, artifact, JsonOpts, ct).ConfigureAwait(false);
    return artifact;
  }

  public async Task<GameArtifact?> GetAsync(string id, CancellationToken ct = default) {
    var path = GetPathForId(id);
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
    var path = GetPathForId(id);
    if (!File.Exists(path)) return Task.FromResult(false);
    File.Delete(path);
    return Task.FromResult(true);
  }
}
