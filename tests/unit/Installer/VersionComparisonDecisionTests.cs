using FluentAssertions;
using GameBot.Domain.Versioning;
using Xunit;

namespace GameBot.UnitTests.Installer;

public sealed class VersionComparisonDecisionTests
{
  private static readonly SemanticVersionComparer Comparer = new();

  [Theory]
  [InlineData("1.0.0.0", "0.9.9.9", "upgrade")]
  [InlineData("1.0.0.0", "1.0.0.0", "sameBuild")]
  [InlineData("1.0.0.0", "1.0.0.1", "downgrade")]
  [InlineData("2.0.0.0", "1.9.9.999", "upgrade")]
  public void CompareMatrixProducesExpectedOutcome(string candidateRaw, string installedRaw, string expected)
  {
    var candidate = SemanticVersion.Parse(candidateRaw);
    var installed = SemanticVersion.Parse(installedRaw);

    var result = ResolveOutcome(candidate, installed);

    result.Should().Be(expected);
  }

  private static string ResolveOutcome(SemanticVersion candidate, SemanticVersion installed)
  {
    var compare = Comparer.Compare(candidate, installed);
    if (compare < 0)
    {
      return "downgrade";
    }

    if (compare > 0)
    {
      return "upgrade";
    }

    return "sameBuild";
  }
}
