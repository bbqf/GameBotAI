namespace GameBot.Service.Models.Installer;

using GameBot.Domain.Installer;

internal sealed class InstallerOptions {
  public const string SectionName = "Service:Installer";
  public string DefaultInstallMode { get; set; } = "backgroundApp";
  public int DefaultBackendPort { get; set; } = 5000;
  public List<int> PreferredWebPorts { get; set; } = [8080, 8088, 8888, 80];
  public string DefaultProtocol { get; set; } = "http";
}

internal sealed class InstallerPreflightRequest {
  public string InstallMode { get; set; } = "backgroundApp";
  public int BackendPort { get; set; } = 5000;
  public int? RequestedWebUiPort { get; set; }
  public string Protocol { get; set; } = "http";
  public bool Unattended { get; set; }
  public bool StartOnLogin { get; set; } = true;
  public bool ConfirmFirewallFallback { get; set; } = true;
}

internal sealed class InstallerPreflightResponse {
  public bool CanProceed { get; set; }
  public bool RequiresElevation { get; set; }
  public List<string> Warnings { get; set; } = [];
  public int SelectedWebUiPort { get; set; }
  public List<int> SuggestedWebUiPorts { get; set; } = [];
  public string StartupPolicy { get; set; } = "manual";
  public string FirewallScope { get; set; } = "privateNetworkOnly";
  public PortProbeResult? PortProbe { get; set; }
}
