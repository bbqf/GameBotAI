using GameBot.Domain.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class PrerequisiteScanner {
  public static Task<IReadOnlyList<PrerequisiteStatus>> ScanAsync(CancellationToken ct = default) {
    var statuses = new List<PrerequisiteStatus> {
      new() {
        PrerequisiteKey = "dotnet-runtime",
        DisplayName = ".NET Runtime",
        RequiredVersion = "9.0",
        DetectedVersion = Environment.Version.ToString(),
        State = PrerequisiteState.Detected,
        Source = PrerequisiteSource.System
      }
    };

    return Task.FromResult<IReadOnlyList<PrerequisiteStatus>>(statuses);
  }
}
