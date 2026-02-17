using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class PortSelectionRulesTests {
  [Fact]
  public void PortDetectionAndPropertiesContainDeterministicPortOrder() {
    var repoRoot = FindRepoRoot();
    var propertiesPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");
    var detectionPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "PortDetection.wxs");
    var resolverScriptPath = Path.Combine(repoRoot, "installer", "wix", "Scripts", "PortResolver.js");

    File.Exists(propertiesPath).Should().BeTrue();
    File.Exists(detectionPath).Should().BeTrue();
    File.Exists(resolverScriptPath).Should().BeTrue();

    var propertiesContent = File.ReadAllText(propertiesPath);
    var detectionContent = File.ReadAllText(detectionPath);
    var resolverScriptContent = File.ReadAllText(resolverScriptPath);

    propertiesContent.Should().Contain("8080,8088,8888,80");
    detectionContent.Should().Contain("DetectAvailablePorts");
    resolverScriptContent.Should().Contain("PREFERRED_WEB_PORT_ORDER");
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
