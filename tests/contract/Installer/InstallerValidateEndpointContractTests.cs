using FluentAssertions;
using Xunit;

namespace GameBot.ContractTests.Installer;

public class InstallerValidateEndpointContractTests {
  [Fact]
  public void InstallerContractIncludesValidateEndpointAndValidationResultSchema() {
    var repoRoot = FindRepoRoot();
    var contractPath = Path.Combine(repoRoot, "specs", "025-standalone-windows-installer", "contracts", "installer.openapi.yaml");

    var content = File.ReadAllText(contractPath);
    content.Should().Contain("/installer/validate:");
    content.Should().Contain("ValidationResult:");
    content.Should().Contain("resolvedPort");
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
