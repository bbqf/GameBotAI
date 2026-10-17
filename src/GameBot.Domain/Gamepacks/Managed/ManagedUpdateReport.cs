using GameBot.Domain.Gamepacks.Signing;

namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>The values of the report field <c>result</c>.</summary>
public static class ManagedUpdateResults {
  /// <summary>The call ended and all steps worked.</summary>
  public const string Done = "done";

  /// <summary>A dry run with no problem.</summary>
  public const string Planned = "planned";

  /// <summary>A step failed after the start.</summary>
  public const string Failed = "failed";

  /// <summary>A check ended the call before any change.</summary>
  public const string Rejected = "rejected";
}

/// <summary>The reason names of a managed call. An error body has one of these names in the field <c>reason</c>.</summary>
public static class ManagedUpdateReasons {
  /// <summary>The request is malformed.</summary>
  public const string InvalidRequest = "invalid_request";

  /// <summary>The file is not valid JSON.</summary>
  public const string InvalidFile = "invalid_file";

  /// <summary>The signature policy rejects the file.</summary>
  public const string SignatureRejected = "signature_rejected";

  /// <summary>The check found problems.</summary>
  public const string ValidationFailed = "validation_failed";

  /// <summary>A name in the template list matches no template.</summary>
  public const string TemplateNotFound = "template_not_found";

  /// <summary>No gamepack has this name.</summary>
  public const string GamepackNotFound = "gamepack_not_found";

  /// <summary>The gamepack has no kept previous version.</summary>
  public const string NoPreviousVersion = "no_previous_version";

  /// <summary>The catalog does not list the entry.</summary>
  public const string EntryNotListed = "entry_not_listed";

  /// <summary>No catalog location is set.</summary>
  public const string NotConfigured = "not_configured";

  /// <summary>Another catalog failure.</summary>
  public const string CatalogError = "catalog_error";

  /// <summary>The request is larger than the limit.</summary>
  public const string TooLarge = "too_large";

  /// <summary>A managed call runs on the instance.</summary>
  public const string UpdateInProgress = "update_in_progress";

  /// <summary>A queue did not stop in the time limit.</summary>
  public const string StopTimeout = "stop_timeout";

  /// <summary>The backup step failed.</summary>
  public const string BackupFailed = "backup_failed";

  /// <summary>The install step failed.</summary>
  public const string InstallFailed = "install_failed";

  /// <summary>The step that books the new entries failed.</summary>
  public const string BookingFailed = "booking_failed";

  /// <summary>A queue did not start. Used in <see cref="RestoreStep"/> text only.</summary>
  public const string RestartFailed = "restart_failed";

  /// <summary>The rollback was refused.</summary>
  public const string RollbackRefused = "rollback_refused";

  /// <summary>An unknown failure.</summary>
  public const string Unexpected = "unexpected";
}

/// <summary>A queue in the report.</summary>
/// <param name="Id">The queue id.</param>
/// <param name="Name">The queue name.</param>
/// <param name="TemplateName">The name of the linked template, or null.</param>
/// <param name="Error">The error text for a queue that did not start, or null.</param>
public sealed record QueueRef(string Id, string Name, string? TemplateName, string? Error = null);

/// <summary>An entry in the report.</summary>
/// <param name="TemplateName">The template name.</param>
/// <param name="SequenceName">The sequence name.</param>
/// <param name="Position">The zero-based position of the entry in the template.</param>
public sealed record EntryRef(string TemplateName, string SequenceName, int Position);

/// <summary>One step of a restore.</summary>
/// <param name="Step">The step name.</param>
/// <param name="Outcome">The text <c>ok</c> or <c>failed</c>.</param>
/// <param name="Error">The error text, or null.</param>
public sealed record RestoreStep(string Step, string Outcome, string? Error = null);

/// <summary>The restore part of the report.</summary>
public sealed class RestoreReport {
  /// <summary>True when the flow ran at least one restore step.</summary>
  public bool Happened { get; init; }

  /// <summary>True when all restore steps worked. False when no restore step ran.</summary>
  public bool Restored { get; init; }

  /// <summary>The restore steps in order.</summary>
  public IReadOnlyList<RestoreStep> Steps { get; init; } = Array.Empty<RestoreStep>();

  /// <summary>The objects that the restore could not put back.</summary>
  public IReadOnlyList<string> FailedObjects { get; init; } = Array.Empty<string>();

  /// <summary>The report of a call that ran no restore.</summary>
  public static RestoreReport None { get; } = new();
}

/// <summary>The result of a managed call (feature 148).</summary>
public sealed class ManagedUpdateReport {
  /// <summary>One of <see cref="ManagedUpdateResults"/>.</summary>
  public string Result { get; init; } = ManagedUpdateResults.Done;

  /// <summary>One of <see cref="ManagedUpdateReasons"/>, or null for <c>done</c> and <c>planned</c>.</summary>
  public string? Reason { get; init; }

  /// <summary>The error text of a failed call, or null.</summary>
  public string? Detail { get; init; }

  /// <summary>The echo of the request flag.</summary>
  public bool DryRun { get; init; }

  /// <summary>The target gamepack name, or null.</summary>
  public string? GamepackName { get; init; }

  /// <summary>The target gamepack version, or null.</summary>
  public string? GamepackVersion { get; init; }

  /// <summary>The version that was installed before the call, or null.</summary>
  public string? PreviousVersion { get; init; }

  /// <summary>The queues that the flow stopped. For a dry run: the queues that it would stop.</summary>
  public IReadOnlyList<QueueRef> StoppedQueues { get; init; } = Array.Empty<QueueRef>();

  /// <summary>The queues that run again.</summary>
  public IReadOnlyList<QueueRef> RestartedQueues { get; init; } = Array.Empty<QueueRef>();

  /// <summary>The queues that did not start.</summary>
  public IReadOnlyList<QueueRef> NotRestartedQueues { get; init; } = Array.Empty<QueueRef>();

  /// <summary>The id of the stored backup, or null.</summary>
  public string? BackupId { get; init; }

  /// <summary>The number of created objects (planned for a dry run).</summary>
  public int Created { get; init; }

  /// <summary>The number of updated objects (planned for a dry run).</summary>
  public int Updated { get; init; }

  /// <summary>The number of removed objects (planned for a dry run).</summary>
  public int Removed { get; init; }

  /// <summary>The number of user objects taken over (planned for a dry run). 0 when the option is off (feature 149).</summary>
  public int TakenOver { get; init; }

  /// <summary>
  /// The id of the take-over backup (feature 149). Not set for a dry run, for a call that replaced nothing, and for a failed call.
  /// <see cref="BackupId"/> keeps its meaning: the queue template backup.
  /// </summary>
  public string? TakeOverBackupId { get; init; }

  /// <summary>The list of objects to take over. Set for a dry run with the option on and no problem. Empty otherwise.</summary>
  public IReadOnlyList<TakeOverEntry> TakeOvers { get; init; } = Array.Empty<TakeOverEntry>();

  /// <summary>The names of the seed templates created (planned for a dry run). Always set (feature 150).</summary>
  public IReadOnlyList<string> SeedTemplatesCreated { get; init; } = Array.Empty<string>();

  /// <summary>The seed templates not created (planned for a dry run). Always set (feature 150).</summary>
  public IReadOnlyList<GameBot.Domain.Gamepacks.Seeds.SeedSkip> SeedTemplatesSkipped { get; init; } = Array.Empty<GameBot.Domain.Gamepacks.Seeds.SeedSkip>();

  /// <summary>The new disabled entries (planned for a dry run).</summary>
  public IReadOnlyList<EntryRef> NewEntries { get; init; } = Array.Empty<EntryRef>();

  /// <summary>The entries whose sequence the new version removed. The entries stay.</summary>
  public IReadOnlyList<EntryRef> RemovedSequenceEntries { get; init; } = Array.Empty<EntryRef>();

  /// <summary>The templates in the request list that the gamepack does not affect.</summary>
  public IReadOnlyList<string> NotAffectedTemplates { get; init; } = Array.Empty<string>();

  /// <summary>All problems of the check.</summary>
  public IReadOnlyList<GamepackProblem> Problems { get; init; } = Array.Empty<GamepackProblem>();

  /// <summary>The signature block, or null.</summary>
  public SignatureCheckResult? Signature { get; init; }

  /// <summary>The restore part.</summary>
  public RestoreReport Restore { get; init; } = RestoreReport.None;
}
