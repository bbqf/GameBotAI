namespace GameBot.Domain.Gamepacks;

/// <summary>Reads and writes the gamepack registry and the previous-version files.</summary>
public interface IGamepackRegistryStore {
  /// <summary>Reads the registry. A missing file gives an empty registry. Each call gives a new copy.</summary>
  GamepackRegistry Load();

  /// <summary>True when the registry file exists.</summary>
  bool RegistryExists { get; }

  /// <summary>Deletes the registry file. A missing file is not an error.</summary>
  void DeleteRegistry();

  /// <summary>Writes the registry with write-to-temp and atomic replace.</summary>
  /// <param name="registry">The registry to write.</param>
  void Save(GamepackRegistry registry);

  /// <summary>Reads the registry, applies a change, and writes it, as one locked step.</summary>
  /// <param name="change">The change.</param>
  void Update(Action<GamepackRegistry> change);

  /// <summary>Reads the text of the previous-version file, or null when the file is missing.</summary>
  /// <param name="name">The gamepack name.</param>
  string? ReadPrevious(string name);

  /// <summary>Writes the previous-version file with write-to-temp and atomic replace.</summary>
  /// <param name="name">The gamepack name.</param>
  /// <param name="text">The file text.</param>
  void WritePrevious(string name, string text);

  /// <summary>Deletes the previous-version file. A missing file is not an error.</summary>
  /// <param name="name">The gamepack name.</param>
  void DeletePrevious(string name);

  /// <summary>Gives the full path of the registry file, or null when the store has no file. It has no side effect.</summary>
  string? RegistryFilePath => null;

  /// <summary>Gives the full path of the previous-version file for <paramref name="name"/>, or null when the store has no file or the name is not valid. It has no side effect.</summary>
  /// <param name="name">The gamepack name.</param>
  string? PreviousFilePath(string name) => null;
}
