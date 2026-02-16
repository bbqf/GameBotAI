using FluentAssertions;
using Xunit;

namespace GameBot.ContractTests.Installer;

public class InstallerPropertyContractTests {
  [Fact]
  public void InstallerOpenApiContractDefinesCanonicalInstallRequestFields() {
    var repoRoot = FindRepoRoot();
    var contractPath = Path.Combine(repoRoot, "specs", "025-standalone-windows-installer", "contracts", "installer.openapi.yaml");

    File.Exists(contractPath).Should().BeTrue();
    var content = File.ReadAllText(contractPath);

    content.Should().Contain("InstallRequest:");
    content.Should().Contain("installMode:");
    content.Should().Contain("installScope:");
    content.Should().Contain("dataRoot:");
    content.Should().Contain("backendPort:");
    content.Should().Contain("webUiPort:");
    content.Should().Contain("protocol:");
    content.Should().Contain("/installer/validate:");
    content.Should().Contain("/installer/execute:");
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
