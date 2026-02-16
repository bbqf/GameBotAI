using FluentAssertions;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class InstallerPropertyValidationTests {
  [Fact]
  public void InstallerPropertiesFragmentDefinesCanonicalProperties() {
    var repoRoot = FindRepoRoot();
    var filePath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");

    File.Exists(filePath).Should().BeTrue();
    var content = File.ReadAllText(filePath);

    content.Should().Contain("Property Id=\"MODE\"");
    content.Should().Contain("Property Id=\"SCOPE\"");
    content.Should().Contain("Property Id=\"DATA_ROOT\"");
    content.Should().Contain("Property Id=\"BACKEND_PORT\"");
    content.Should().Contain("Property Id=\"WEB_PORT\"");
    content.Should().Contain("Property Id=\"PROTOCOL\"");
  }

  [Fact]
  public void CommonInstallerModuleContainsScopeDefaultDataRootResolver() {
    var repoRoot = FindRepoRoot();
    var filePath = Path.Combine(repoRoot, "scripts", "installer", "common.psm1");

    File.Exists(filePath).Should().BeTrue();
    var content = File.ReadAllText(filePath);

    content.Should().Contain("function Get-DefaultDataRoot");
    content.Should().Contain("ValidateSet(\"perMachine\", \"perUser\")");
    content.Should().Contain("ProgramData");
    content.Should().Contain("LocalAppData");
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
