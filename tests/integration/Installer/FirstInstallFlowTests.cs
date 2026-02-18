using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class FirstInstallFlowTests {
  [Fact]
  public void ProductDefinitionContainsBaselineInstallAndFeatureWiring() {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");

    File.Exists(productPath).Should().BeTrue();
    var content = File.ReadAllText(productPath);

    content.Should().Contain("<Package");
    content.Should().Contain("<Feature Id=\"MainFeature\"");
    content.Should().Contain("<ComponentGroupRef Id=\"GameBotFiles\" />");
    content.Should().Contain("<ComponentGroupRef Id=\"DataDirectoryComponents\" />");
    content.Should().Contain("<ComponentGroupRef Id=\"StartupRegistrationComponents\" />");
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
