using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class PortSelectionRulesTests {
  [Fact]
  public void PortValidationFragmentContainsDeterministicPortOrder() {
    var repoRoot = FindRepoRoot();
    var filePath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "PortValidation.wxs");

    File.Exists(filePath).Should().BeTrue();
    var content = File.ReadAllText(filePath);

    content.Should().Contain("8080,8088,8888,80");
    content.Should().Contain("ValidatePortSelection");
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
