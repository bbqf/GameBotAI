namespace GameBot.Domain.Installer;

public enum InstallationRunStatus {
  Success,
  Failed,
  Aborted
}

public sealed class InstallerExecutionResult {
  public string RunId { get; set; } = string.Empty;
  public InstallationRunStatus Status { get; set; } = InstallationRunStatus.Aborted;
  public string SelectedProfileId { get; set; } = string.Empty;
  public EndpointConfiguration? EndpointConfiguration { get; set; }
  public IReadOnlyList<PrerequisiteStatus> Prerequisites { get; set; } = [];
  public IReadOnlyList<string> Warnings { get; set; } = [];
  public IReadOnlyList<string> Errors { get; set; } = [];
  public DateTimeOffset CompletedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
