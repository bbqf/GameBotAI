namespace GameBot.Service.Contracts.Backup;

/// <summary>Selection of commands and sequences to include in a backup archive.</summary>
internal sealed class BackupRequestDto {
  public IReadOnlyList<string> CommandIds { get; init; } = Array.Empty<string>();
  public IReadOnlyList<string> SequenceIds { get; init; } = Array.Empty<string>();
}
