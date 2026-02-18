using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class SameBuildUnattendedStatusTests
{
  [Fact]
  public void UnattendedSameBuildFlowUsesDedicatedStatusCode()
  {
    var repoRoot = FindRepoRoot();
    var programPath = Path.Combine(repoRoot, "src", "GameBot.Service", "Program.cs");
    var silentScriptPath = Path.Combine(repoRoot, "scripts", "installer", "silent-install-examples.ps1");

    var program = File.ReadAllText(programPath);
    var script = File.ReadAllText(silentScriptPath);

    program.Should().Contain("Action = \"skip\"");
    program.Should().Contain("StatusCode = 4090");
    script.Should().Contain("status code 4090");
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
