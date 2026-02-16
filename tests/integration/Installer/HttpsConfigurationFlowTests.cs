using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class HttpsConfigurationFlowTests {
  [Fact]
  public void HttpsConfigurationFragmentContainsHttpsToggleAndCertificateValidation() {
    var repoRoot = FindRepoRoot();
    var httpsPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "HttpsConfiguration.wxs");

    File.Exists(httpsPath).Should().BeTrue();
    var content = File.ReadAllText(httpsPath);

    content.Should().Contain("ENABLE_HTTPS");
    content.Should().Contain("CERTIFICATE_REF");
    content.Should().Contain("HTTPS_REQUIRES_CERTIFICATE");
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
