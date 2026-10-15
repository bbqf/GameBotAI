using GameBot.Domain.Commands;

namespace GameBot.Domain.Gamepacks;

/// <summary>One object that the apply writes.</summary>
public sealed class PlannedObject {
  /// <summary>One of <see cref="GamepackObjectTypes"/>.</summary>
  public required string Type { get; init; }

  /// <summary>The object name. For an image, the id.</summary>
  public required string Name { get; init; }

  /// <summary>The id that the stored object has after the apply.</summary>
  public required string FinalId { get; init; }

  /// <summary>The id of the stored object that the apply updates, or null when the apply creates the object.</summary>
  public string? ExistingId { get; init; }

  /// <summary>True when the apply updates an existing object of the same gamepack.</summary>
  public bool IsUpdate => ExistingId is not null;

  /// <summary>The sequence, for a sequence object. References are already rewritten.</summary>
  public CommandSequence? Sequence { get; init; }

  /// <summary>The command, for a command object. References are already rewritten.</summary>
  public Command? Command { get; init; }

  /// <summary>The image entry, for an image object.</summary>
  public GamepackImage? Image { get; init; }

  /// <summary>The decoded image bytes, for an image object.</summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The decoded image bytes go to a stream. A copy would waste memory.")]
  public byte[]? ImageBytes { get; init; }

  /// <summary>The key used in the write gate and in logs.</summary>
  public string Key => GamepackRegistry.ObjectKey(Type, Name);
}

/// <summary>One stored object that the apply deletes.</summary>
/// <param name="Type">One of <see cref="GamepackObjectTypes"/>.</param>
/// <param name="Name">The object name.</param>
/// <param name="Id">The stored id.</param>
public sealed record PlannedRemoval(string Type, string Name, string Id) {
  /// <summary>The key used in the write gate and in logs.</summary>
  public string Key => GamepackRegistry.ObjectKey(Type, Name);
}

/// <summary>What an install or a rollback does.</summary>
public sealed class GamepackPlan {
  /// <summary>The manifest of the version that the apply installs.</summary>
  public required GamepackManifest Manifest { get; init; }

  /// <summary>The objects to write, in write order: images, commands, then sequences.</summary>
  public required IReadOnlyList<PlannedObject> Writes { get; init; }

  /// <summary>The owned objects to delete, in delete order: sequences, commands, then images.</summary>
  public required IReadOnlyList<PlannedRemoval> Removals { get; init; }

  /// <summary>The number of objects that the apply creates.</summary>
  public int Created => Writes.Count(w => !w.IsUpdate);

  /// <summary>The number of objects that the apply updates.</summary>
  public int Updated => Writes.Count(w => w.IsUpdate);

  /// <summary>The number of objects that the apply removes.</summary>
  public int Removed => Removals.Count;
}

/// <summary>The result of the shared check (dry run, install, and rollback).</summary>
public sealed class GamepackValidationResult {
  /// <summary>All problems. The check does not stop at the first.</summary>
  public IReadOnlyList<GamepackProblem> Problems { get; init; } = Array.Empty<GamepackProblem>();

  /// <summary>The plan. It is set only when there is no problem.</summary>
  public GamepackPlan? Plan { get; init; }

  /// <summary>True when there is no problem.</summary>
  public bool IsValid => Problems.Count == 0;
}
