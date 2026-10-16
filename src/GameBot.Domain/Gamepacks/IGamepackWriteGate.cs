namespace GameBot.Domain.Gamepacks;

/// <summary>The write operations that the applier reports to the write gate.</summary>
public static class GamepackWriteOperations {
  /// <summary>An object is created.</summary>
  public const string Create = "create";

  /// <summary>An object is changed.</summary>
  public const string Update = "update";

  /// <summary>An object is deleted.</summary>
  public const string Delete = "delete";

  /// <summary>The previous-version file is written or deleted.</summary>
  public const string PreviousFile = "previous-file";

  /// <summary>The registry is written.</summary>
  public const string Registry = "registry";

  /// <summary>One step of the restore after a failure.</summary>
  public const string Restore = "restore";

  /// <summary>The take-over backup file is written (feature 149).</summary>
  public const string TakeOverBackup = "takeover-backup";

  /// <summary>One write of a restore of a take-over backup (feature 149).</summary>
  public const string TakeOverRestore = "takeover-restore";
}

/// <summary>
/// Test seam (feature 143). The applier calls the gate before each write. A test gate can throw to simulate a
/// fault. Production uses <see cref="NoOpGamepackWriteGate"/>.
/// </summary>
public interface IGamepackWriteGate {
  /// <summary>Called before a write.</summary>
  /// <param name="operation">One of <see cref="GamepackWriteOperations"/>.</param>
  /// <param name="key">The object key, or the gamepack name for a file write.</param>
  void BeforeWrite(string operation, string key);
}

/// <summary>The production gate. It does nothing.</summary>
public sealed class NoOpGamepackWriteGate : IGamepackWriteGate {
  /// <inheritdoc />
  public void BeforeWrite(string operation, string key) {
    // No action. Tests replace this gate.
  }
}
