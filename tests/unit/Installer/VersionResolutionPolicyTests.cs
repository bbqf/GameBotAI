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
      CiBuildCounter = new CiBuildCounter { LastBuild = 100, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Ci
    });

    result.Version.Should().Be(new SemanticVersion(1, 7, 2, 101));
    result.Notes.Should().Contain("minor:override");
    result.Notes.Should().Contain("patch:override");
  }

  [Fact]
  public void ResolveUsesBaselineMinorAndPatchWhenNoOverride()
  {
    var result = VersionResolutionService.Resolve(new VersionResolutionInput
    {
      BaselineVersion = new SemanticVersion(2, 3, 8, 45),
      Override = new VersionOverride(),
      CiBuildCounter = new CiBuildCounter { LastBuild = 45, UpdatedAtUtc = DateTimeOffset.UtcNow, UpdatedBy = "ci" },
      Context = BuildContext.Ci
    });

    result.Version.Should().Be(new SemanticVersion(2, 3, 8, 46));
    result.Notes.Should().Contain("minor:baseline");
    result.Notes.Should().Contain("patch:baseline");
  }
}
