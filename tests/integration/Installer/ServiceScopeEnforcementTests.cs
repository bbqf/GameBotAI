using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class ServiceScopeEnforcementTests {
  [Fact]
  public void ProductIsPerUserOnlyWithoutServiceModeScopeRule() {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");

    var content = File.ReadAllText(productPath);
    content.Should().Contain("Scope=\"perUser\"");
    content.Should().NotContain("SERVICE_MODE_REQUIRES_PER_MACHINE");
    content.Should().NotContain("Service mode requires per-machine scope");
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
