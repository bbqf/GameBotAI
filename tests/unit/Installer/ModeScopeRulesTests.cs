using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class ModeScopeRulesTests {
  [Fact]
  public void ValidationFragmentContainsServicePerMachineRule() {
    var repoRoot = FindRepoRoot();
    var filePath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "Validation.wxs");

    File.Exists(filePath).Should().BeTrue();
    var content = File.ReadAllText(filePath);

    content.Should().Contain("SERVICE_MODE_REQUIRES_PER_MACHINE");
    content.Should().Contain("MODE = \"service\"");
    content.Should().Contain("SCOPE = \"perMachine\"");
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
