using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class StartupRegistrationTests {
  [Fact]
  public void StartupRegistrationFragmentContainsBackgroundRunEntry() {
    var repoRoot = FindRepoRoot();
    var startupPath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "StartupRegistration.wxs");

    File.Exists(startupPath).Should().BeTrue();
    var content = File.ReadAllText(startupPath);

    content.Should().Contain("StartupRegistrationComponents");
    content.Should().Contain("BackgroundStartupRegistryComponent");
    content.Should().Contain("Name=\"GameBot\"");
    content.Should().Contain("CurrentVersion\\Run");
    content.Should().Contain("[APPLICATIONFOLDER]GameBot.Service.exe");
    content.Should().Contain("Run");
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
