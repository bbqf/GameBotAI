using System.Text.Json;
using System.Text.RegularExpressions;
using System.Diagnostics.CodeAnalysis;

namespace GameBot.Domain.Commands;

public sealed class FileCommandRepository : ICommandRepository {
  private readonly string _dir;
  private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web) {
    WriteIndented = true
  };
  private static readonly Regex SafeIdPattern = new("^[A-Za-z0-9_-]+$", RegexOptions.Compiled);

  public FileCommandRepository(string root) {
    _dir = Path.Combine(root, "commands");
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

  public async Task<Command> AddAsync(Command command, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(command);
    var id = string.IsNullOrWhiteSpace(command.Id) ? Guid.NewGuid().ToString("N") : command.Id;
    command.Id = id;
    if (!TryGetSafePath(id, out var path)) {
      throw new InvalidOperationException("Generated or provided command ID is invalid for file storage.");
    }
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, command, JsonOpts, ct).ConfigureAwait(false);
    return command;
  }

  public async Task<Command?> GetAsync(string id, CancellationToken ct = default) {
    if (!TryGetSafePath(id, out var path)) return null;
    if (!File.Exists(path)) return null;
    using var fs = File.OpenRead(path);
    return await JsonSerializer.DeserializeAsync<Command>(fs, JsonOpts, ct).ConfigureAwait(false);
  }

  public async Task<IReadOnlyList<Command>> ListAsync(CancellationToken ct = default) {
    var list = new List<Command>();
    foreach (var file in Directory.EnumerateFiles(_dir, "*.json")) {
      using var fs = File.OpenRead(file);
      var item = await JsonSerializer.DeserializeAsync<Command>(fs, JsonOpts, ct).ConfigureAwait(false);
      if (item is not null) list.Add(item);
    }
    return list;
  }

  public async Task<Command?> UpdateAsync(Command command, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(command);
    if (string.IsNullOrWhiteSpace(command.Id)) return null;
    if (!TryGetSafePath(command.Id, out var path)) return null;
    if (!File.Exists(path)) return null;
    using var fs = File.Create(path);
    await JsonSerializer.SerializeAsync(fs, command, JsonOpts, ct).ConfigureAwait(false);
    return command;
  }

  public Task<bool> DeleteAsync(string id, CancellationToken ct = default) {
    if (!TryGetSafePath(id, out var path)) return Task.FromResult(false);
    if (!File.Exists(path)) return Task.FromResult(false);
    File.Delete(path);
    return Task.FromResult(true);
  }
}
