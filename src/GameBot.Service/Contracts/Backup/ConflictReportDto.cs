namespace GameBot.Service.Contracts.Backup;

/// <summary>Result of a dry-run restore: lists objects in the archive that conflict with existing data.</summary>
internal sealed class ConflictReportDto {
  public bool HasConflicts { get; init; }
  public IReadOnlyList<string> ConflictingCommandNames { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> ConflictingSequenceNames { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> ConflictingImageIds { get; init; } = Array.Empty<string>();
  public int TotalCommands { get; init; }
  public int TotalSequences { get; init; }
  public int TotalImages { get; init; }
}
