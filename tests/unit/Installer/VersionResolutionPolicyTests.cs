using FluentAssertions;
using GameBot.Domain.Versioning;
using Xunit;

namespace GameBot.UnitTests.Installer;

public sealed class VersionResolutionPolicyTests
{
  [Fact]
  public void ResolveUsesOverrideMinorAndPatchWhenProvided()
  {
    var result = VersionResolutionService.Resolve(new VersionResolutionInput
    {
      BaselineVersion = new SemanticVersion(1, 5, 9, 100),
      Override = new VersionOverride { Minor = 7, Patch = 2 },
      ReleaseLineTransitionDetected = true,
      PreviousReleaseLineSequence = 3,
      CurrentReleaseLineSequence = 4,
      CiBuildCounter = new CiBuildCounter { LastBuild = 100, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Ci
    });

    result.Version.Should().Be(new SemanticVersion(1, 7, 2, 101));
    result.Notes.Should().Contain("minor:override");
    result.Notes.Should().Contain("patch:override");
  }

  [Fact]
  public void ResolveResetsPatchOnReleaseLineTransitionWhenPatchNotOverridden()
  {
    var result = VersionResolutionService.Resolve(new VersionResolutionInput
    {
      BaselineVersion = new SemanticVersion(2, 3, 8, 45),
      Override = new VersionOverride(),
      ReleaseLineTransitionDetected = false,
      PreviousReleaseLineSequence = 10,
      CurrentReleaseLineSequence = 11,
      CiBuildCounter = new CiBuildCounter { LastBuild = 45, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Ci
    });

    result.Version.Should().Be(new SemanticVersion(2, 4, 0, 46));
    result.Notes.Should().Contain("minor:auto-transition");
    result.Notes.Should().Contain("patch:auto-reset");
  }

  [Theory]
  [InlineData(0, 1, true)]
  [InlineData(2, 2, false)]
  [InlineData(5, 3, false)]
  public void HasReleaseLineTransitionComparesSequenceMonotonically(int previous, int current, bool expected)
  {
    VersionResolutionService.HasReleaseLineTransition(previous, current).Should().Be(expected);
  }
}
