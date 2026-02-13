using GameBot.Domain.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class BackendNetworkConfigurator {
  public static EndpointConfiguration Configure(string protocol, int backendPort, int webUiPort, string firewallScope) {
    return new EndpointConfiguration {
      BackendHostScope = "allInterfaces",
      BackendPort = backendPort,
      WebUiPort = webUiPort,
      WebUiPortSelectionOrder = InstallerDefaults.PreferredWebPorts,
      FirewallScope = firewallScope,
      Protocol = protocol,
      AnnouncedBackendUrl = new Uri($"{protocol}://0.0.0.0:{backendPort}"),
      AnnouncedWebUiUrl = new Uri($"{protocol}://localhost:{webUiPort}")
    };
  }
}
