using System.Text;
using System.Text.Json;
using GameBot.Domain.Utils;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// File-backed <see cref="IGamepackRegistryStore"/> (feature 143). It keeps <c>&lt;dataRoot&gt;/gamepacks/registry.json</c>
/// and <c>gamepacks/previous/&lt;name&gt;.json</c>. A write goes to a temporary file first and then replaces the file.
/// </summary>
public sealed class GamepackRegistryStore : IGamepackRegistryStore {
  /// <summary>The folder name under the data root.</summary>
  public const string FolderName = "gamepacks";

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
  };

  // The error text for the registry file names the kind "registry". The registry file holds all packs, so it has no pack id.
  private const string RegistryKind = "registry";
  private const string RegistryId = "registry.json";

  private readonly string _folder;
  private readonly string _previousFolder;
  private readonly string _registryPath;
  private readonly object _lock = new();

  /// <summary>Creates the store on <paramref name="dataRoot"/>.</summary>
  /// <param name="dataRoot">The data root.</param>
  public GamepackRegistryStore(string dataRoot) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _folder = Path.Combine(dataRoot, FolderName);
    _previousFolder = Path.Combine(_folder, "previous");
    _registryPath = Path.Combine(_folder, "registry.json");
  }

  /// <summary>The path of the registry file.</summary>
  public string RegistryPath => _registryPath;

  /// <inheritdoc />
  public bool RegistryExists => File.Exists(_registryPath);

  /// <inheritdoc />
  public void DeleteRegistry() {
    lock (_lock) {
      if (File.Exists(_registryPath)) {
        File.Delete(_registryPath);
      }
    }
  }

  /// <inheritdoc />
  public GamepackRegistry Load() {
    lock (_lock) {
      return LoadUnlocked();
    }
  }

  /// <inheritdoc />
  public void Save(GamepackRegistry registry) {
    ArgumentNullException.ThrowIfNull(registry);
    lock (_lock) {
      WriteAtomic(_registryPath, JsonSerializer.Serialize(registry, JsonOptions), RegistryKind, RegistryId);
    }
  }

  /// <inheritdoc />
  public void Update(Action<GamepackRegistry> change) {
    ArgumentNullException.ThrowIfNull(change);
    lock (_lock) {
      var registry = LoadUnlocked();
      change(registry);
      WriteAtomic(_registryPath, JsonSerializer.Serialize(registry, JsonOptions), RegistryKind, RegistryId);
    }
  }

  /// <inheritdoc />
  public string? ReadPrevious(string name) {
    var path = PreviousPath(name);
    lock (_lock) {
      return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
    }
  }

  /// <inheritdoc />
  public void WritePrevious(string name, string text) {
    ArgumentNullException.ThrowIfNull(text);
    var path = PreviousPath(name);
    lock (_lock) {
      WriteAtomic(path, text, "previous", name);
    }
  }

  /// <inheritdoc />
  public string? RegistryFilePath => Path.GetFullPath(_registryPath);

  /// <inheritdoc />
  public string? PreviousFilePath(string name) => GamepackNameRules.IsValid(name) ? Path.GetFullPath(PreviousPath(name)) : null;

  /// <inheritdoc />
  public void DeletePrevious(string name) {
    var path = PreviousPath(name);
    lock (_lock) {
      if (File.Exists(path)) {
        File.Delete(path);
      }
    }
  }

  private string PreviousPath(string name) {
    if (!GamepackNameRules.IsValid(name)) {
      throw new ArgumentException("The gamepack name is not valid.", nameof(name));
    }

    return Path.Combine(_previousFolder, GamepackNameRules.Key(name) + ".json");
  }

  private GamepackRegistry LoadUnlocked() {
    if (!File.Exists(_registryPath)) {
      return new GamepackRegistry();
    }

    var text = File.ReadAllText(_registryPath, Encoding.UTF8);
    if (string.IsNullOrWhiteSpace(text)) {
      return new GamepackRegistry();
    }

    return JsonSerializer.Deserialize<GamepackRegistry>(text, JsonOptions) ?? new GamepackRegistry();
  }

  private static void WriteAtomic(string path, string text, string kind, string id) {
    var dir = Path.GetDirectoryName(path)!;
    Directory.CreateDirectory(dir);
    var temp = Path.Combine(dir, Path.GetFileName(path) + "." + Guid.NewGuid().ToString("N") + ".tmp");
    File.WriteAllText(temp, text, new UTF8Encoding(false));
    AtomicFile.ReplaceFile(temp, path, kind, id);
  }
}
