using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks.Seeds;

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

  /// <summary>True when the apply writes over an existing stored object (an update of the same gamepack, or a take-over).</summary>
  public bool IsUpdate => ExistingId is not null;

  /// <summary>
  /// True when the write replaces an object of the user (feature 149). <see cref="ExistingId"/> is the id of that object.
  /// It is true only when <see cref="ExistingId"/> is set.
  /// </summary>
  public bool IsTakeOver { get; init; }

  /// <summary>The sequence, for a sequence object. References are already rewritten.</summary>
  public CommandSequence? Sequence { get; init; }

  /// <summary>The command, for a command object. References are already rewritten.</summary>
  public Command? Command { get; init; }

  /// <summary>The image entry, for an image object.</summary>
  public GamepackImage? Image { get; init; }

  /// <summary>The decoded image bytes, for an image object.</summary>
  [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "The decoded image bytes go to a stream. A copy would waste memory.")]
  public byte[]? ImageBytes { get; init; }

  /// <summary>The stored sequence that a take-over replaces, as the validator read it (feature 149). Null for other writes.</summary>
  public CommandSequence? LiveSequence { get; init; }

  /// <summary>The stored command that a take-over replaces, as the validator read it (feature 149). Null for other writes.</summary>
  public Command? LiveCommand { get; init; }

  /// <summary>
  /// The content hash of the object that the apply writes (feature 149). The validator sets it for a take-over write, so the
  /// applier does not compute it a second time. Null for other writes.
  /// </summary>
  public string? PostHash { get; set; }

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

  /// <summary>The number of objects of the same gamepack that the apply updates. A take-over is not an update.</summary>
  public int Updated => Writes.Count(w => w.IsUpdate && !w.IsTakeOver);

  /// <summary>The number of user objects that the apply takes over (feature 149).</summary>
  public int TakenOver => Writes.Count(w => w.IsTakeOver);

  /// <summary>One entry for each take-over write, with the mark identical or different. The validator sets it.</summary>
  public IReadOnlyList<TakeOverEntry> TakeOvers { get; init; } = Array.Empty<TakeOverEntry>();

  /// <summary>The number of objects that the apply removes.</summary>
  public int Removed => Removals.Count;

  /// <summary>The seed templates (feature 150). Empty when the install is not a first install, or for a rollback.</summary>
  public SeedPlan Seeds { get; init; } = SeedPlan.Empty;

  /// <summary>The number of seed templates that the apply creates.</summary>
  public int SeedsToCreate => Seeds.Items.Count(i => i.Outcome == SeedOutcomes.Create);

  /// <summary>The number of seed templates that the plan skips.</summary>
  public int SeedsSkipped => Seeds.Items.Count(i => i.Outcome == SeedOutcomes.SkipExists);
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
