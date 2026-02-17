using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class PayloadValidationTests {
  [Fact]
  public void PackagePayloadScriptCreatesServiceAndWebUiPayloadStructure() {
    var repoRoot = FindRepoRoot();
    var scriptPath = Path.Combine(repoRoot, "scripts", "package-installer-payload.ps1");

    File.Exists(scriptPath).Should().BeTrue();

    var script = File.ReadAllText(scriptPath);
    script.Should().Contain("installer/wix/payload");
    script.Should().Contain("service");
    script.Should().Contain("web-ui");
    script.Should().Contain("payload-manifest.json");
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
