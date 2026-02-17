using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class ModeScopeRulesTests {
  [Fact]
  public void ProductAndPropertiesEnforcePerUserScopeWithoutServiceModeRules() {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var propsPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");

    File.Exists(productPath).Should().BeTrue();
    File.Exists(propsPath).Should().BeTrue();
    var productContent = File.ReadAllText(productPath);
    var propsContent = File.ReadAllText(propsPath);

    productContent.Should().Contain("Scope=\"perUser\"");
    propsContent.Should().Contain("SetProperty Id=\"WixAppFolder\" Value=\"WixPerUserFolder\"");
    productContent.Should().NotContain("MODE");
    productContent.Should().NotContain("SCOPE");
    productContent.Should().NotContain("SERVICE_MODE_REQUIRES_PER_MACHINE");
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
