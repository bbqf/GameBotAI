using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class AllowlistSourcePolicyTests {
  [Fact]
  public void PrerequisitePolicyFragmentContainsAllowlistEnforcement() {
    var repoRoot = FindRepoRoot();
    var policyPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "PrerequisitePolicy.wxs");

    var content = File.ReadAllText(policyPath);
    content.Should().Contain("ALLOWLISTED_PREREQ_SOURCES");
    content.Should().Contain("NON_ALLOWLISTED_SOURCE_BLOCKED");
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
