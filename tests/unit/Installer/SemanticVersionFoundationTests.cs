using FluentAssertions;
using GameBot.Domain.Versioning;
using Xunit;

namespace GameBot.UnitTests.Installer;

public sealed class SemanticVersionFoundationTests
{
  [Fact]
  public void SemanticVersionTryParseParsesValidFourPartVersion()
  {
    var ok = SemanticVersion.TryParse("1.2.3.4", out var version);

    ok.Should().BeTrue();
    version.Major.Should().Be(1);
    version.Minor.Should().Be(2);
    version.Patch.Should().Be(3);
    version.Build.Should().Be(4);
  }

  [Theory]
  [InlineData("")]
  [InlineData("1.2.3")]
  [InlineData("1.2.3.-1")]
  [InlineData("a.b.c.d")]
  public void SemanticVersionTryParseRejectsInvalidInputs(string raw)
  {
    var ok = SemanticVersion.TryParse(raw, out _);

    ok.Should().BeFalse();
  }

  [Fact]
  public void SemanticVersionComparerUsesMajorMinorPatchBuildOrder()
  {
    var comparer = new SemanticVersionComparer();

    comparer.Compare(new SemanticVersion(1, 0, 0, 0), new SemanticVersion(1, 0, 0, 1)).Should().BeLessThan(0);
    comparer.Compare(new SemanticVersion(1, 4, 9, 9), new SemanticVersion(2, 0, 0, 0)).Should().BeLessThan(0);
    comparer.Compare(new SemanticVersion(3, 1, 0, 0), new SemanticVersion(3, 1, 0, 0)).Should().Be(0);
  }

  [Fact]
  public void VersionResolutionServiceCiBuildIncrementsAndPersists()
  {
    var result = VersionResolutionService.Resolve(new VersionResolutionInput
    {
      BaselineVersion = new SemanticVersion(1, 2, 3, 10),
      Override = new VersionOverride(),
      ReleaseLineTransitionDetected = false,
      CiBuildCounter = new CiBuildCounter { LastBuild = 10, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Ci
    });

    result.Version.Should().Be(new SemanticVersion(1, 2, 3, 11));
    result.ShouldPersistBuildCounter.Should().BeTrue();
    result.IsAuthoritativeBuild.Should().BeTrue();
  }

  [Fact]
  public void VersionResolutionServiceLocalBuildDerivesWithoutPersisting()
  {
    var result = VersionResolutionService.Resolve(new VersionResolutionInput
    {
      BaselineVersion = new SemanticVersion(1, 2, 3, 20),
      Override = new VersionOverride(),
      ReleaseLineTransitionDetected = false,
      CiBuildCounter = new CiBuildCounter { LastBuild = 20, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Local
    });

    result.Version.Should().Be(new SemanticVersion(1, 2, 3, 21));
    result.ShouldPersistBuildCounter.Should().BeFalse();
    result.IsAuthoritativeBuild.Should().BeFalse();
  }

  [Fact]
  public void VersionResolutionServiceAutoMinorTransitionResetsPatchWithoutOverride()
  {
    var result = VersionResolutionService.Resolve(new VersionResolutionInput
    {
      BaselineVersion = new SemanticVersion(2, 5, 7, 30),
      Override = new VersionOverride { Major = 2 },
      ReleaseLineTransitionDetected = true,
      CiBuildCounter = new CiBuildCounter { LastBuild = 30, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Ci
    });

    result.Version.Should().Be(new SemanticVersion(2, 6, 0, 31));
  }
}
