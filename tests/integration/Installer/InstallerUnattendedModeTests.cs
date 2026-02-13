using FluentAssertions;
using GameBot.Service.Models.Installer;
using GameBot.Service.Services.Installer;
using Xunit;

namespace GameBot.IntegrationTests.Installer;

public class InstallerUnattendedModeTests {
  [Fact]
  public async Task ExecuteAsyncReturnsSuccessForValidUnattendedArguments() {
    InstallerPreflightRequest? capturedRequest = null;
    var coordinator = new InstallerCliCoordinator(
      (request, _) => {
        capturedRequest = request;
        return Task.FromResult(new InstallerCliExecutionResponse {
          Success = true,
          Message = "ok"
        });
      });

    var result = await coordinator.ExecuteAsync([
      "--mode", "backgroundApp",
      "--backend-port", "5000",
      "--protocol", "http",
      "--unattended"
    ]).ConfigureAwait(false);

    result.ExitCode.Should().Be(0);
    capturedRequest.Should().NotBeNull();
    capturedRequest!.Unattended.Should().BeTrue();
  }
}
