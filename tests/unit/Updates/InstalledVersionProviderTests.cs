using FluentAssertions;
using GameBot.Domain.Versioning;
using GameBot.Service.Services.Updates;
using Xunit;

namespace GameBot.UnitTests.Updates;

public sealed class InstalledVersionProviderTests {
  [Fact]
  public void PlainFourPartVersionParses() {
    new InstalledVersionProvider("1.7.0.412").GetInstalledVersion().Should().Be(new SemanticVersion(1, 7, 0, 412));
  }

  [Fact]
  public void CommitSuffixIsRemoved() {
    new InstalledVersionProvider("1.7.0.412+abc123").GetInstalledVersion().Should().Be(new SemanticVersion(1, 7, 0, 412));
  }

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("1.0.0")]
  [InlineData("1.0.0+abc")]
  [InlineData("not a version")]
  public void UnknownVersionGivesZero(string? value) {
    new InstalledVersionProvider(value).GetInstalledVersion().Should().Be(new SemanticVersion(0, 0, 0, 0));
  }

  [Fact]
  public void DefaultConstructorReadsTheServiceAssembly() {
    var act = () => new InstalledVersionProvider().GetInstalledVersion();

    act.Should().NotThrow();
  }
}
