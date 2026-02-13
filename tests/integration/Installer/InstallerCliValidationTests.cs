using FluentAssertions;
using GameBot.Service.Services.Installer;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class InstallerCliValidationTests {
  [Fact]
  public async Task ExecuteAsyncReturnsActionableRemediationForInvalidArguments() {
    var coordinator = new InstallerCliCoordinator(
      (_, _) => Task.FromResult(new InstallerCliExecutionResponse {
        Success = true,
        Message = "should-not-be-used"
      }));

    var result = await coordinator.ExecuteAsync([
      "--mode", "invalid-mode",
      "--backend-port", "5000",
      "--unattended"
    ]).ConfigureAwait(false);

    result.ExitCode.Should().Be(2);
    result.Message.Should().Contain("Remediation");
  }
}
