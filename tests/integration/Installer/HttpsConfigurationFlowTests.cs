using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class HttpsConfigurationFlowTests {
  [Fact]
  public void FrontendShortcutUsesHttpOnlyWithDynamicWebPort() {
    var repoRoot = FindRepoRoot();
    var httpsPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "HttpsConfiguration.wxs");
    var directoriesPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "Directories.wxs");
    var bundlePath = Path.Combine(repoRoot, "installer", "wix", "Bundle.wxs");

    File.Exists(httpsPath).Should().BeFalse();
    var directoriesContent = File.ReadAllText(directoriesPath);
    var bundleContent = File.ReadAllText(bundlePath);

    directoriesContent.Should().Contain("url.dll,FileProtocolHandler http://[SHORTCUT_HOST]:[PORT]/");
    bundleContent.Should().NotContain("ENABLE_HTTPS");
    bundleContent.Should().NotContain("CERTIFICATE_REF");
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
