using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class LocalBuildDerivationTests
{
  [Fact]
  public void LocalBuildDerivesVersionWithoutCounterPersistence()
  {
    var repoRoot = FindRepoRoot();
    var commonModulePath = Path.Combine(repoRoot, "scripts", "installer", "common.psm1");
    var buildScriptPath = Path.Combine(repoRoot, "scripts", "build-installer.ps1");

    var common = File.ReadAllText(commonModulePath);
    var buildScript = File.ReadAllText(buildScriptPath);

    common.Should().Contain("$nextBuild = $counter + 1");
    common.Should().Contain("Persisted = ($BuildContext -eq \"ci\")");
    buildScript.Should().Contain("$buildContext = if ($isCi) { \"ci\" } else { \"local\" }");
    buildScript.Should().Contain("Resolved installer version:");
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
