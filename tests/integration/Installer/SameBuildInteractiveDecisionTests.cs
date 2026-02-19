using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class SameBuildInteractiveDecisionTests
{
  [Fact]
  public void ProgramContainsInteractiveSameBuildDecisionFlow()
  {
    var repoRoot = FindRepoRoot();
    var programPath = Path.Combine(repoRoot, "src", "GameBot.Service", "Program.cs");

    var content = File.ReadAllText(programPath);
    content.Should().Contain("/installer/same-build/decision");
    content.Should().Contain("InteractiveChoice");
    content.Should().Contain("Action = \"reinstall\"");
    content.Should().Contain("Action = \"cancel\"");
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
