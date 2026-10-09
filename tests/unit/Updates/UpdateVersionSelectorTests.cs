using FluentAssertions;
using GameBot.Domain.Updates;
using GameBot.Domain.Versioning;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class UpdateVersionSelectorTests {
  [Theory]
  [InlineData("v1.7.0.430", true)]
  [InlineData("V1.7.0.430", true)]
  [InlineData("1.7.0.430", false)]
  [InlineData("v1.7.0", false)]
  [InlineData("v1.7.0.x", false)]
  [InlineData("v", false)]
  [InlineData("", false)]
  [InlineData(null, false)]
  [InlineData("release-2026", false)]
  public void TagParsesOnlyWhenItHasFourParts(string? tag, bool expected) {
    UpdateVersionSelector.TryParseTag(tag, out var version).Should().Be(expected);
    if (expected) {
      version.Should().Be(new SemanticVersion(1, 7, 0, 430));
    }
  }

  [Fact]
  public void HigherReleaseIsAnUpdate() {
    var installed = new SemanticVersion(1, 7, 0, 412);
    UpdateVersionSelector.IsUpdateAvailable(installed, new SemanticVersion(1, 7, 0, 430)).Should().BeTrue();
    UpdateVersionSelector.Evaluate(installed, new SemanticVersion(2, 0, 0, 1)).Should().Be(UpdateCheckStatus.UpdateAvailable);
  }

  [Fact]
  public void EqualReleaseIsNotAnUpdate() {
    var installed = new SemanticVersion(1, 7, 0, 412);
    UpdateVersionSelector.IsUpdateAvailable(installed, installed).Should().BeFalse();
    UpdateVersionSelector.Evaluate(installed, installed).Should().Be(UpdateCheckStatus.UpToDate);
  }

  [Fact]
  public void LowerReleaseIsNotAnUpdate() {
    var installed = new SemanticVersion(1, 7, 0, 412);
    UpdateVersionSelector.IsUpdateAvailable(installed, new SemanticVersion(1, 6, 9, 999)).Should().BeFalse();
    UpdateVersionSelector.Evaluate(installed, new SemanticVersion(1, 7, 0, 411)).Should().Be(UpdateCheckStatus.UpToDate);
  }
}
