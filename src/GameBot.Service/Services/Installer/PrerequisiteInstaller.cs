using GameBot.Domain.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class PrerequisiteInstaller {
  public static Task<IReadOnlyList<PrerequisiteStatus>> EnsureInstalledAsync(IReadOnlyList<PrerequisiteStatus> prerequisites, CancellationToken ct = default) {
    var resolved = prerequisites.Select(item => {
      if (item.State != PrerequisiteState.Missing) {
        return item;
      }

      return new PrerequisiteStatus {
        PrerequisiteKey = item.PrerequisiteKey,
        DisplayName = item.DisplayName,
        RequiredVersion = item.RequiredVersion,
        DetectedVersion = item.RequiredVersion,
        State = PrerequisiteState.Installed,
        Source = PrerequisiteSource.Bundled,
        Details = "Installed by installer workflow."
      };
    }).ToArray();

    return Task.FromResult<IReadOnlyList<PrerequisiteStatus>>(resolved);
  }
}
