using System.Text.Json.Serialization;

namespace GameBot.Domain.QueueSettings;

/// <summary>The outcome values of one import item.</summary>
public static class ImportOutcomes {
  /// <summary>The import made a new object.</summary>
  public const string Created = "created";

  /// <summary>The import changed an object.</summary>
  public const string Updated = "updated";

  /// <summary>The object already has the document values. No write.</summary>
  public const string Unchanged = "unchanged";

  /// <summary>A write failed for the object (HTTP 500 only, FR-018).</summary>
  public const string Failed = "failed";

  /// <summary>The import did not touch the object.</summary>
  public const string Skipped = "skipped";
}

/// <summary>The reason texts of an import item.</summary>
public static class ImportReasons {
  /// <summary>A gamepack owns the object on this instance (FR-006).</summary>
  public const string OwnedByGamepack = "owned_by_gamepack";
}

/// <summary>The result for one object of the document.</summary>
public sealed class ImportItem {
  /// <summary>One of <see cref="SettingsObjectTypes"/>.</summary>
  public string Type { get; init; } = string.Empty;

  /// <summary>The object name.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>One of <see cref="ImportOutcomes"/>.</summary>
  public string Outcome { get; init; } = string.Empty;

  /// <summary>A short reason, or null.</summary>
  public string? Reason { get; init; }

  /// <summary>True for an updated queue that is not stopped (FR-016).</summary>
  public bool RestartRequired { get; init; }

  /// <summary>Gives a copy of the item with another outcome and reason.</summary>
  /// <param name="outcome">The new outcome.</param>
  /// <param name="reason">The new reason.</param>
  public ImportItem With(string outcome, string? reason) => new() {
    Type = Type,
    Name = Name,
    Outcome = outcome,
    Reason = reason,
    RestartRequired = RestartRequired
  };
}

/// <summary>A link whose target is missing on the instance (FR-008).</summary>
public sealed class UnresolvedReference {
  /// <summary>One of <see cref="SettingsObjectTypes"/>.</summary>
  public string Type { get; init; } = string.Empty;

  /// <summary>The name of the missing target.</summary>
  public string Name { get; init; } = string.Empty;

  /// <summary>The user of the target as <c>type:name</c>.</summary>
  public string UsedBy { get; init; } = string.Empty;

  /// <summary>The gamepack name from the document, or null.</summary>
  public string? Gamepack { get; init; }

  /// <summary>The gamepack version from the document, or null.</summary>
  public string? Version { get; init; }
}

/// <summary>The counts of the items.</summary>
public sealed class ImportSummary {
  /// <summary>The number of created items.</summary>
  public int Created { get; init; }

  /// <summary>The number of updated items.</summary>
  public int Updated { get; init; }

  /// <summary>The number of unchanged items.</summary>
  public int Unchanged { get; init; }

  /// <summary>The number of skipped items.</summary>
  public int Skipped { get; init; }
}

/// <summary>
/// The import report. On a repository failure (HTTP 500, FR-018) <see cref="Items"/> holds the items that the import
/// applied, <see cref="Failed"/> holds the item that failed, and <see cref="NotApplied"/> holds the items that it did
/// not try.
/// </summary>
public sealed class ImportReport {
  /// <summary>The items. One for each object of the document.</summary>
  public IReadOnlyList<ImportItem> Items { get; init; } = Array.Empty<ImportItem>();

  /// <summary>The links with a missing target.</summary>
  public IReadOnlyList<UnresolvedReference> UnresolvedReferences { get; init; } = Array.Empty<UnresolvedReference>();

  /// <summary>The counts.</summary>
  public ImportSummary Summary { get; init; } = new();

  /// <summary>The item that failed, or null.</summary>
  [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
  public ImportItem? Failed { get; init; }

  /// <summary>The items that the import did not try after a failure.</summary>
  public IReadOnlyList<ImportItem> NotApplied { get; init; } = Array.Empty<ImportItem>();
}

/// <summary>The status of an import request.</summary>
public enum ImportStatus {
  /// <summary>The import ran. The report has the items.</summary>
  Ok,

  /// <summary>The document is not valid. Nothing changed.</summary>
  Invalid,

  /// <summary>A repository write failed. The report is partial.</summary>
  WriteFailed
}

/// <summary>The result of an import request.</summary>
public sealed class ImportOutcome {
  /// <summary>The status.</summary>
  public ImportStatus Status { get; init; }

  /// <summary>The errors of an invalid document.</summary>
  public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

  /// <summary>The report. It is set for <see cref="ImportStatus.Ok"/> and <see cref="ImportStatus.WriteFailed"/>.</summary>
  public ImportReport? Report { get; init; }
}
