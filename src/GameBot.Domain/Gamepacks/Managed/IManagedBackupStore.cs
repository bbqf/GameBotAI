using GameBot.Domain.QueueSettings;

namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>The store for the backup file of a managed call (feature 148).</summary>
public interface IManagedBackupStore {
  /// <summary>True when the text has the form of a backup id. The check has no file access.</summary>
  /// <param name="id">The text to check.</param>
  bool IsValidId(string? id);

  /// <summary>Writes a backup and returns its id.</summary>
  /// <param name="gamepackName">The validated gamepack name.</param>
  /// <param name="document">The settings document of the affected templates.</param>
  /// <param name="ct">The cancellation token.</param>
  Task<string> WriteAsync(string gamepackName, SettingsDocument document, CancellationToken ct = default);

  /// <summary>Reads the text of a backup, or null when the id is not valid or the file is missing.</summary>
  /// <param name="id">The backup id.</param>
  string? Read(string id);
}
