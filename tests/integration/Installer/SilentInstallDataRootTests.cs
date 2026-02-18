using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class SilentInstallDataRootTests {
  [Fact]
  public void BundleMapsSilentDataRootPropertyToMsi() {
    var repoRoot = FindRepoRoot();
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");

    var content = File.ReadAllText(bundlePath);
    content.Should().Contain("MsiProperty Name=\"DATA_ROOT\"");
    content.Should().Contain("Variable Name=\"DATA_ROOT\"");
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
