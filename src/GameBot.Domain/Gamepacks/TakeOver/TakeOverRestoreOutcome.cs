namespace GameBot.Domain.Gamepacks;

/// <summary>How strict a restore of a take-over backup is.</summary>
public enum TakeOverRestoreMode {
  /// <summary>Checks <c>object_missing</c>, <c>object_changed</c>, and <c>owner_changed</c>. The public route uses it.</summary>
  Strict,

  /// <summary>Skips <c>owner_changed</c> only. Only the managed update uses it, inside its own gate.</summary>
  Force
}

/// <summary>The status of a restore of a take-over backup.</summary>
public enum TakeOverRestoreStatus {
  /// <summary>All objects are back.</summary>
  Restored,

  /// <summary>The id is not valid, or no backup has this id.</summary>
  NotFound,

  /// <summary>A check failed. Nothing changed.</summary>
  Refused,

  /// <summary>A write failed and the state is as before.</summary>
  Failed,

  /// <summary>A write failed and the undo failed too.</summary>
  PartialRestore
}

/// <summary>The reason codes of a refused restore. There are exactly three.</summary>
public static class TakeOverRestoreReasonCodes {
  /// <summary>A backed-up object no longer exists.</summary>
  public const string ObjectMissing = "object_missing";

  /// <summary>The live object differs from the content that the install wrote.</summary>
  public const string ObjectChanged = "object_changed";

  /// <summary>The gamepack no longer owns the object, or the gamepack is not installed.</summary>
  public const string OwnerChanged = "owner_changed";
}

/// <summary>One reason for a refused restore.</summary>
/// <param name="Code">One of <see cref="TakeOverRestoreReasonCodes"/>.</param>
/// <param name="Type">The object type.</param>
/// <param name="Name">The object name.</param>
/// <param name="Message">The message.</param>
public sealed record TakeOverRestoreReason(string Code, string Type, string Name, string Message);

/// <summary>The result of a restore of a take-over backup.</summary>
public sealed class TakeOverRestoreOutcome {
  /// <summary>The status.</summary>
  public TakeOverRestoreStatus Status { get; init; }

  /// <summary>The backup id from the request.</summary>
  public string BackupId { get; init; } = string.Empty;

  /// <summary>The number of objects that were put back.</summary>
  public int Restored { get; init; }

  /// <summary>One reason for each refused object.</summary>
  public IReadOnlyList<TakeOverRestoreReason> Reasons { get; init; } = Array.Empty<TakeOverRestoreReason>();

  /// <summary>The error text of a failed write, or null.</summary>
  public string? ErrorMessage { get; init; }
}
