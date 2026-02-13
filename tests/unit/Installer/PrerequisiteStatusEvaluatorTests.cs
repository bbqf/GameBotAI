using FluentAssertions;
using GameBot.Domain.Installer;
using GameBot.Service.Services.Installer;
using Xunit;

namespace GameBot.UnitTests.Installer;

public class PrerequisiteStatusEvaluatorTests {
  [Fact]
  public async Task MissingPrerequisiteTransitionsToInstalled() {
    var input = new[] {
      new PrerequisiteStatus {
        PrerequisiteKey = "python",
        DisplayName = "Python",
        State = PrerequisiteState.Missing,
        Source = PrerequisiteSource.System
      }
    };

    var result = await PrerequisiteInstaller.EnsureInstalledAsync(input).ConfigureAwait(false);
    result.Should().ContainSingle();
    result[0].State.Should().Be(PrerequisiteState.Installed);
    result[0].Source.Should().Be(PrerequisiteSource.Bundled);
  }
}
