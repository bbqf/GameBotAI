using GameBot.Service.Models.Installer;

namespace GameBot.UnitTests.Helpers;

internal static class InstallerTestBuilder {
  internal static InstallerPreflightRequest CreateValidPreflightRequest() {
    return new InstallerPreflightRequest {
      InstallMode = "backgroundApp",
      BackendPort = 5000,
      RequestedWebUiPort = 8080,
      Protocol = "http",
      Unattended = true
    };
  }
}
