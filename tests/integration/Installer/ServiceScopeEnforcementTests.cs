using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class ServiceScopeEnforcementTests {
  [Fact]
  public void ValidationFragmentContainsServiceScopeLaunchCondition() {
    var repoRoot = FindRepoRoot();
    var validationPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "Validation.wxs");

    var content = File.ReadAllText(validationPath);
    content.Should().Contain("SERVICE_MODE_REQUIRES_PER_MACHINE");
    content.Should().Contain("Service mode requires per-machine scope");
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
