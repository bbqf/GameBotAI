namespace GameBot.Domain.Logging;

/// <summary>
/// Feature 198 (FR-006, FR-007): the close rule for an entry that has the status "running" from an
/// earlier service process. The run of such an entry ended when the service stopped.
/// </summary>
public static class ExecutionLogAbandonedRun {
  /// <summary>The maximum length of the summary (the same limit as the other summaries).</summary>
  public const int MaxSummaryLength = 240;

  /// <summary>
  /// Returns true when the entry has the status "running" and started before
  /// <paramref name="processStartUtc"/>.
  /// </summary>
  public static bool IsAbandoned(ExecutionLogEntry entry, DateTimeOffset processStartUtc) {
    ArgumentNullException.ThrowIfNull(entry);
    return string.Equals(entry.FinalStatus, "running", StringComparison.OrdinalIgnoreCase)
      && entry.TimestampUtc < processStartUtc;
  }

  /// <summary>
  /// Returns a copy of the entry with the status "failure" and the summary
  /// "... run ended when the service stopped.". All other fields stay the same.
  /// Returns the same entry when its status is not "running".
  /// </summary>
  public static ExecutionLogEntry Close(ExecutionLogEntry entry) {
    ArgumentNullException.ThrowIfNull(entry);
    if (!string.Equals(entry.FinalStatus, "running", StringComparison.OrdinalIgnoreCase)) {
      return entry;
    }

    return new ExecutionLogEntry {
      Id = entry.Id,
      TimestampUtc = entry.TimestampUtc,
      ExecutionType = entry.ExecutionType,
      FinalStatus = "failure",
      ObjectRef = entry.ObjectRef,
      Navigation = entry.Navigation,
      Hierarchy = entry.Hierarchy,
      Summary = BuildSummary(entry),
      Details = entry.Details,
      StepOutcomes = entry.StepOutcomes,
      RetentionExpiresUtc = entry.RetentionExpiresUtc,
      RotatedToExecutionId = entry.RotatedToExecutionId,
      RotatedFromExecutionId = entry.RotatedFromExecutionId,
      CancellationReason = entry.CancellationReason,
      TimeLimitMs = entry.TimeLimitMs,
      Origin = entry.Origin
    };
  }

  private static string BuildSummary(ExecutionLogEntry entry) {
    var name = string.IsNullOrWhiteSpace(entry.ObjectRef.DisplayNameSnapshot)
      ? entry.ObjectRef.ObjectId
      : entry.ObjectRef.DisplayNameSnapshot;
    var prefix = (entry.ExecutionType ?? string.Empty).ToUpperInvariant() switch {
      "QUEUE" => "Queue ",
      "SEQUENCE" => "Sequence ",
      _ => string.Empty
    };
    var text = $"{prefix}'{name}' run ended when the service stopped.";
    return text.Length <= MaxSummaryLength ? text : text[..MaxSummaryLength];
  }
}
