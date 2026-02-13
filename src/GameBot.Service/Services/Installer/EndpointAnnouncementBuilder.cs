using GameBot.Domain.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class EndpointAnnouncementBuilder {
  public static EndpointConfiguration Build(string protocol, int backendPort, int webUiPort) {
    return new EndpointConfiguration {
      BackendHostScope = "allInterfaces",
      BackendPort = backendPort,
      WebUiPort = webUiPort,
      WebUiPortSelectionOrder = InstallerDefaults.PreferredWebPorts,
      FirewallScope = "privateNetworkOnly",
      Protocol = protocol,
      AnnouncedBackendUrl = new Uri($"{protocol}://0.0.0.0:{backendPort}"),
      AnnouncedWebUiUrl = new Uri($"{protocol}://localhost:{webUiPort}")
    };
  }
}
