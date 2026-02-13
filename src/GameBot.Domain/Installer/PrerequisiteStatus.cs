namespace GameBot.Domain.Installer;

public enum PrerequisiteState {
  Detected,
  Missing,
  Installed,
  Failed,
  Skipped
}

public enum PrerequisiteSource {
  System,
  Bundled,
  Online
}

public sealed class PrerequisiteStatus {
  public string PrerequisiteKey { get; set; } = string.Empty;
  public string DisplayName { get; set; } = string.Empty;
  public string? RequiredVersion { get; set; }
  public string? DetectedVersion { get; set; }
  public PrerequisiteState State { get; set; } = PrerequisiteState.Missing;
  public PrerequisiteSource Source { get; set; } = PrerequisiteSource.System;
  public string? Details { get; set; }
}
