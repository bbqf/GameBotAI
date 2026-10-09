using GameBot.Domain.Versioning;

namespace GameBot.Domain.Updates;

/// <summary>
/// Chooses if a release is an update. A release is an update only when its version is greater
/// than the installed version (FR-002, FR-008). The bot never offers an equal or a lower version.
/// </summary>
public static class UpdateVersionSelector {
  private static readonly SemanticVersionComparer Comparer = new();

  /// <summary>Parse a tag of the form v&lt;major&gt;.&lt;minor&gt;.&lt;patch&gt;.&lt;build&gt;. A tag that does not parse gives false.</summary>
  public static bool TryParseTag(string? tag, out SemanticVersion version) {
    version = default;
    if (string.IsNullOrWhiteSpace(tag)) {
      return false;
    }

    var trimmed = tag.Trim();
    if (trimmed.Length < 2 || (trimmed[0] != 'v' && trimmed[0] != 'V')) {
      return false;
    }

    return SemanticVersion.TryParse(trimmed.Substring(1), out version);
  }

  /// <summary>True only when the release version is greater than the installed version.</summary>
  public static bool IsUpdateAvailable(SemanticVersion installed, SemanticVersion release) =>
    Comparer.Compare(release, installed) > 0;

  /// <summary>Return UpdateAvailable or UpToDate for a release version.</summary>
  public static UpdateCheckStatus Evaluate(SemanticVersion installed, SemanticVersion release) =>
    IsUpdateAvailable(installed, release) ? UpdateCheckStatus.UpdateAvailable : UpdateCheckStatus.UpToDate;
}
