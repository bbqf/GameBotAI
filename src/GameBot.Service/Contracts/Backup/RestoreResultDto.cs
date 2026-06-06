namespace GameBot.Service.Contracts.Backup;

/// <summary>Result of applying a restore operation.</summary>
internal sealed class RestoreResultDto {
  public int RestoredCommands { get; init; }
  public int RestoredSequences { get; init; }
  public int RestoredImages { get; init; }
  /// <summary>True if the apply partially failed and a rollback was attempted.</summary>
  public bool RolledBack { get; init; }
  public string? ErrorMessage { get; init; }
}
