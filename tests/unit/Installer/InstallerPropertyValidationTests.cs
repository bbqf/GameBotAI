using FluentAssertions;
using Xunit;
using System.Text.RegularExpressions;

namespace GameBot.UnitTests.Installer;

public class InstallerPropertyValidationTests {
  [Fact]
  public void InstallerPropertiesFragmentDefinesCanonicalProperties() {
    var repoRoot = FindRepoRoot();
    var filePath = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");

    File.Exists(filePath).Should().BeTrue();
    var content = File.ReadAllText(filePath);
    var propertyIds = Regex.Matches(content, "Property Id=\"([^\"]+)\"")
      .Select(match => match.Groups[1].Value)
      .Distinct(StringComparer.OrdinalIgnoreCase)
      .ToHashSet(StringComparer.OrdinalIgnoreCase);

    propertyIds.Should().Contain("DATA_ROOT");
    propertyIds.Should().Contain("PORT");
    propertyIds.Should().Contain("PERSISTED_PORT");
    propertyIds.Should().Contain("ALLOW_ONLINE_PREREQ_FALLBACK");
    propertyIds.Should().NotContain("MODE");
    propertyIds.Should().NotContain("SCOPE");
  }

  [Fact]
  public void CommonInstallerModuleContainsScopeDefaultDataRootResolver() {
    var repoRoot = FindRepoRoot();
    var filePath = Path.Combine(repoRoot, "scripts", "installer", "common.psm1");

    File.Exists(filePath).Should().BeTrue();
    var content = File.ReadAllText(filePath);

    content.Should().Contain("function Get-DefaultDataRoot");
    content.Should().Contain("ValidateSet(\"perUser\")");
    content.Should().NotContain("perMachine");
    content.Should().NotContain("ProgramData");
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
