using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// Stores the backup of a take-over as <c>&lt;dataRoot&gt;\gamepacks\takeover-backups\&lt;id&gt;.json</c> (feature 149).
/// The id is <c>to-yyyyMMddTHHmmssfff-&lt;8 hex&gt;</c>. The store checks the id form before every file access. It writes
/// to a temporary file and moves it over the target. It has no retention rule.
/// </summary>
public sealed class TakeOverBackupStore : ITakeOverBackupStore {
  private static readonly Regex IdPattern = new("^to-[0-9]{8}T[0-9]{9}-[0-9a-f]{8}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };

  private readonly string _folder;
  private readonly TimeProvider _time;

  /// <summary>Creates the store.</summary>
  /// <param name="dataRoot">The data root.</param>
  /// <param name="timeProvider">The clock, or null.</param>
  public TakeOverBackupStore(string dataRoot, TimeProvider? timeProvider = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _folder = Path.Combine(dataRoot, "gamepacks", "takeover-backups");
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <inheritdoc />
  public bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

  /// <inheritdoc />
  public string Write(TakeOverBackup backup) {
    ArgumentNullException.ThrowIfNull(backup);
    Directory.CreateDirectory(_folder);
    var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
    var suffix = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
    var id = "to-" + stamp + "-" + suffix;
    backup.Id = id;
    var target = PathOf(id);
    var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try {
      File.WriteAllText(temp, JsonSerializer.Serialize(backup, JsonOptions), new UTF8Encoding(false));
      File.Move(temp, target, overwrite: false);
    }
    finally {
      if (File.Exists(temp)) {
        File.Delete(temp);
      }
    }

    return id;
  }

  /// <inheritdoc />
  public TakeOverBackup? Read(string id) {
    if (!IsValidId(id)) {
      return null;
    }

    var path = PathOf(id);
    if (!File.Exists(path)) {
      return null;
    }

    try {
      return JsonSerializer.Deserialize<TakeOverBackup>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
    }
    catch (JsonException) {
      return null;
    }
  }

  /// <inheritdoc />
  public void Delete(string id) {
    if (!IsValidId(id)) {
      return;
    }

    var path = PathOf(id);
    if (File.Exists(path)) {
      File.Delete(path);
    }
  }

  private string PathOf(string id) => Path.Combine(_folder, id + ".json");
}
