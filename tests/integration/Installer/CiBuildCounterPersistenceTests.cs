using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class CiBuildCounterPersistenceTests
{
  [Fact]
  public void BuildPipelinePersistsCounterOnlyInCiContext()
  {
    var repoRoot = FindRepoRoot();
    var commonModulePath = Path.Combine(repoRoot, "scripts", "installer", "common.psm1");
    var buildScriptPath = Path.Combine(repoRoot, "scripts", "build-installer.ps1");

    var common = File.ReadAllText(commonModulePath);
    var buildScript = File.ReadAllText(buildScriptPath);

    common.Should().Contain("function Set-CiBuildCounter");
    common.Should().Contain("if ($BuildContext -eq \"ci\")");
    buildScript.Should().Contain("$buildContext = if ($isCi) { \"ci\" } else { \"local\" }");
    buildScript.Should().Contain("Resolve-InstallerVersion -RepoRoot $repoRoot -BuildContext $buildContext");
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
