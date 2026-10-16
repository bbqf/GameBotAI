using System.Text;
using System.Text.Json;

namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>
/// File-backed <see cref="IGamepackCatalogSettingsStore"/> (feature 147). It keeps <c>&lt;dataRoot&gt;/gamepack-catalog.json</c>.
/// A read happens on each call. A write goes to a temporary file first and then replaces the file.
/// </summary>
public sealed class GamepackCatalogSettingsStore : IGamepackCatalogSettingsStore {
  /// <summary>The file name in the data root.</summary>
  public const string FileName = "gamepack-catalog.json";

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true
  };

  private readonly string _path;
  private readonly object _lock = new();

  /// <summary>Creates the store on <paramref name="dataRoot"/>.</summary>
  /// <param name="dataRoot">The data root.</param>
  public GamepackCatalogSettingsStore(string dataRoot) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _path = Path.Combine(dataRoot, FileName);
  }

  /// <summary>The path of the settings file.</summary>
  public string FilePath => _path;

  /// <inheritdoc />
  public GamepackCatalogSettings Load() {
    lock (_lock) {
      return LoadUnlocked();
    }
  }

  /// <inheritdoc />
  public GamepackCatalogSettings Save(string? location, bool allowInsecure, string? token) {
    var trimmed = location?.Trim();
    if (!string.IsNullOrEmpty(trimmed) && !CatalogLocation.TryParse(trimmed, allowInsecure, out _, out var error)) {
      throw new CatalogException(CatalogFailure.InvalidLocation, error);
    }

    lock (_lock) {
      var current = LoadUnlocked();
      var settings = new GamepackCatalogSettings {
        Location = string.IsNullOrEmpty(trimmed) ? null : trimmed,
        AllowInsecure = allowInsecure,
        Token = token is null ? current.Token : (token.Length == 0 ? null : token)
      };
      Write(settings);
      return settings;
    }
  }

  private GamepackCatalogSettings LoadUnlocked() {
    if (!File.Exists(_path)) {
      return new GamepackCatalogSettings();
    }

    try {
      var text = File.ReadAllText(_path, Encoding.UTF8);
      return JsonSerializer.Deserialize<GamepackCatalogSettings>(text, JsonOptions) ?? new GamepackCatalogSettings();
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) {
      return new GamepackCatalogSettings();
    }
  }

  private void Write(GamepackCatalogSettings settings) {
    var folder = Path.GetDirectoryName(_path);
    if (!string.IsNullOrEmpty(folder)) {
      Directory.CreateDirectory(folder);
    }

    var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
    try {
      File.Move(temp, _path, overwrite: true);
    }
    catch {
      if (File.Exists(temp)) {
        File.Delete(temp);
      }

      throw;
    }
  }
}
