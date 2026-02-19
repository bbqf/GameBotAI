using FluentAssertions;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public sealed class DowngradeBlockFlowTests
{
  [Fact]
  public void ProductDefinitionAndServiceCompareEndpointContainDowngradeFlowSignals()
  {
    var repoRoot = FindRepoRoot();
    var productPath = Path.Combine(repoRoot, "installer", "wix", "Product.wxs");
    var programPath = Path.Combine(repoRoot, "src", "GameBot.Service", "Program.cs");

    var product = File.ReadAllText(productPath);
    var program = File.ReadAllText(programPath);

    product.Should().Contain("<MajorUpgrade");
    product.Should().Contain("DowngradeErrorMessage=\"A newer version of GameBot is already installed.\"");
    program.Should().Contain("/installer/compare");
    program.Should().Contain("Outcome = \"downgrade\"");
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
