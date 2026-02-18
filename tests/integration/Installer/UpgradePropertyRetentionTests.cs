using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class UpgradePropertyRetentionTests
{
  [Fact]
  public void InstallerFragmentsPersistAndReloadNetworkPropertiesAcrossUpgrade()
  {
    var repoRoot = FindRepoRoot();
    var configTemplates = Path.Combine(repoRoot, "installer", "wix", "Fragments", "ConfigTemplates.wxs");
    var installerProperties = Path.Combine(repoRoot, "installer", "wix", "Fragments", "InstallerProperties.wxs");

    var configContent = File.ReadAllText(configTemplates);
    var propertyContent = File.ReadAllText(installerProperties);

    configContent.Should().Contain("Name=\"BindHost\"");
    configContent.Should().Contain("Name=\"Port\"");
    propertyContent.Should().Contain("Property Id=\"PERSISTED_BIND_HOST\"");
    propertyContent.Should().Contain("Property Id=\"PERSISTED_PORT\"");
    propertyContent.Should().Contain("SetProperty Id=\"BIND_HOST\" Value=\"[PERSISTED_BIND_HOST]\"");
    propertyContent.Should().Contain("SetProperty Id=\"PORT\" Value=\"[PERSISTED_PORT]\"");
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
