using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class AllowlistSourcePolicyTests {
  [Fact]
  public void BundleAndInstallerPropertiesContainPrerequisiteFallbackControl() {
    var repoRoot = FindRepoRoot();
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");
    var propsPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");

    var bundleContent = File.ReadAllText(bundlePath);
    var propsContent = File.ReadAllText(propsPath);

    bundleContent.Should().Contain("Variable Name=\"ALLOW_ONLINE_PREREQ_FALLBACK\"");
    bundleContent.Should().Contain("MsiProperty Name=\"ALLOW_ONLINE_PREREQ_FALLBACK\"");
    propsContent.Should().Contain("Property Id=\"ALLOW_ONLINE_PREREQ_FALLBACK\"");
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
