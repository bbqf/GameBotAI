namespace GameBot.Domain.Gamepacks;

/// <summary>The store for the backup files of a take-over (feature 149).</summary>
public interface ITakeOverBackupStore {
  /// <summary>True when the text has the form of a backup id. The check has no file access.</summary>
  /// <param name="id">The text to check.</param>
  bool IsValidId(string? id);

  /// <summary>Writes a new backup file and returns its id. The store sets the id.</summary>
  /// <param name="backup">The backup.</param>
  string Write(TakeOverBackup backup);

  /// <summary>Reads a backup, or returns null when the id is not valid or the file is missing.</summary>
  /// <param name="id">The backup id.</param>
  TakeOverBackup? Read(string id);

  /// <summary>Removes the file. No error when the id is not valid or the file is missing.</summary>
  /// <param name="id">The backup id.</param>
  void Delete(string id);
}
