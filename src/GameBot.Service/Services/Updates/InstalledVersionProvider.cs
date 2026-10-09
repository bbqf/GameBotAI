using System.Reflection;
using GameBot.Domain.Versioning;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Reads the installed version from the InformationalVersion of the service assembly (research R-005).
/// The build sets it to major.minor.patch.build. The .NET SDK can add a suffix such as "+commit".
/// The provider removes everything from the first "+". A build from source has no 4-part version.
/// It gives 0.0.0.0, so each release counts as newer.
/// </summary>
internal sealed class InstalledVersionProvider : IInstalledVersionProvider {
  private readonly SemanticVersion _version;

  public InstalledVersionProvider()
    : this(typeof(InstalledVersionProvider).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion) { }

  public InstalledVersionProvider(string? informationalVersion) {
    _version = Parse(informationalVersion);
  }

  public SemanticVersion GetInstalledVersion() => _version;

  internal static SemanticVersion Parse(string? informationalVersion) {
    if (string.IsNullOrWhiteSpace(informationalVersion)) {
      return new SemanticVersion(0, 0, 0, 0);
    }

    var plus = informationalVersion.IndexOf('+', StringComparison.Ordinal);
    var text = plus >= 0 ? informationalVersion.Substring(0, plus) : informationalVersion;
    return SemanticVersion.TryParse(text, out var version) ? version : new SemanticVersion(0, 0, 0, 0);
  }
}
