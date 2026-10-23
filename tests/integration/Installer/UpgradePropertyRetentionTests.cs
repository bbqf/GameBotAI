using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class UpgradePropertyRetentionTests {
  [Fact]
  public void InstallerFragmentsKeepOneSavedLocationForTheBindingAcrossUpgrade() {
    var repoRoot = FindRepoRoot();
    var installerProperties = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");
    var portDetection = Path.Combine(repoRoot, "installer", "wix", "Fragments", "PortDetection.wxs");

    var propertyContent = File.ReadAllText(installerProperties);
    var detectionContent = File.ReadAllText(portDetection);

    // The resolver reads the file and the older registry value. No SetProperty row copies a saved value.
    propertyContent.Should().Contain("Property Id=\"NETWORK_REGISTRY_ROOT\"");
    propertyContent.Should().NotContain("PERSISTED_BIND_HOST");
    propertyContent.Should().NotContain("PERSISTED_PORT");
    detectionContent.Should().Contain("Action=\"ResolveNetworkBinding\"");
  }

  private static string FindRepoRoot() {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null) {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln"))) {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }
}
