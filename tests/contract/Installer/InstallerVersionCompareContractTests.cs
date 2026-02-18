using FluentAssertions;
using Xunit;

namespace GameBot.ContractTests.Installer;

public sealed class InstallerVersionCompareContractTests
{
  [Fact]
  public void VersioningContractIncludesInstallerCompareEndpointAndPreservePropertiesField()
  {
    var repoRoot = FindRepoRoot();
    var contractPath = Path.Combine(repoRoot, "specs", "026-installer-semver-upgrade", "contracts", "versioning-installer.openapi.yaml");

    var content = File.ReadAllText(contractPath);
    content.Should().Contain("/installer/compare:");
    content.Should().Contain("InstallCompareResult:");
    content.Should().Contain("preserveProperties");
  }

  private static string FindRepoRoot()
  {
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null)
    {
      if (File.Exists(Path.Combine(dir.FullName, "GameBot.sln")))
      {
        return dir.FullName;
      }

      dir = dir.Parent;
    }

    throw new DirectoryNotFoundException("Unable to locate repository root containing GameBot.sln");
  }
}
