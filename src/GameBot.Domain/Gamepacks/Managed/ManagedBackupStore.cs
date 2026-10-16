using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GameBot.Domain.QueueSettings;

namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>
/// Stores the backup of a managed call as <c>&lt;dataRoot&gt;\gamepacks\backups\&lt;backupId&gt;.json</c> (feature 148).
/// The id is <c>yyyyMMddTHHmmssfff-&lt;gamepack key&gt;</c>. The store checks the id form before any read. It writes to a temporary
/// file and replaces the target. It keeps the newest 20 files.
/// </summary>
public sealed class ManagedBackupStore : IManagedBackupStore {
  /// <summary>The number of backup files that the store keeps.</summary>
  public const int Retention = 20;

  private static readonly Regex IdPattern = new("^[0-9]{8}T[0-9]{9}-[A-Za-z0-9_.-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

  private readonly string _folder;
  private readonly TimeProvider _time;

  /// <summary>Creates the store.</summary>
  /// <param name="dataRoot">The data root.</param>
  /// <param name="timeProvider">The clock, or null.</param>
  public ManagedBackupStore(string dataRoot, TimeProvider? timeProvider = null) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _folder = Path.Combine(dataRoot, "gamepacks", "backups");
    _time = timeProvider ?? TimeProvider.System;
  }

  /// <inheritdoc />
  public bool IsValidId(string? id) => id is not null && IdPattern.IsMatch(id);

  /// <inheritdoc />
  public async Task<string> WriteAsync(string gamepackName, SettingsDocument document, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(document);
    if (!GamepackNameRules.IsValid(gamepackName)) {
      throw new ArgumentException("The gamepack name is not valid.", nameof(gamepackName));
    }

    Directory.CreateDirectory(_folder);
    var stamp = _time.GetUtcNow().UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture);
    var id = stamp + "-" + GamepackNameRules.Key(gamepackName);
    var target = PathOf(id);
    var temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
    try {
      await File.WriteAllTextAsync(temp, SettingsDocumentJson.Serialize(document), new UTF8Encoding(false), ct).ConfigureAwait(false);
      File.Move(temp, target, overwrite: true);
    }
    finally {
      if (File.Exists(temp)) {
        File.Delete(temp);
      }
    }

    Prune();
    return id;
  }

  /// <inheritdoc />
  public string? Read(string id) {
    if (!IsValidId(id)) {
      return null;
    }

    var path = PathOf(id);
    return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
  }

  private string PathOf(string id) => Path.Combine(_folder, id + ".json");

  // The ids start with the time stamp, so the ordinal order of the names is the time order.
  private void Prune() {
    var files = Directory.EnumerateFiles(_folder, "*.json")
      .Where(f => IdPattern.IsMatch(Path.GetFileNameWithoutExtension(f)))
      .OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal)
      .Skip(Retention)
      .ToList();
    foreach (var file in files) {
      try {
        File.Delete(file);
      }
      catch (IOException) {
        // An old backup that stays is not an error. The next write tries again.
      }
    }
  }
}
