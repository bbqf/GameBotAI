namespace GameBot.Domain.Installer;

public sealed class EndpointConfiguration {
  public string BackendHostScope { get; set; } = "allInterfaces";
  public int BackendPort { get; set; }
  public int WebUiPort { get; set; }
  public IReadOnlyList<int> WebUiPortSelectionOrder { get; set; } = [8080, 8088, 8888, 80];
  public string FirewallScope { get; set; } = "privateNetworkOnly";
  public string Protocol { get; set; } = "http";
  public string? CertificateReference { get; set; }
  public Uri AnnouncedWebUiUrl { get; set; } = new("http://localhost:8080", UriKind.Absolute);
  public Uri AnnouncedBackendUrl { get; set; } = new("http://0.0.0.0:5000", UriKind.Absolute);
}

public sealed class PortProbeResult {
  public int RequestedBackendPort { get; set; }
  public int RequestedWebUiPort { get; set; }
  public bool BackendPortAvailable { get; set; }
  public bool WebUiPortAvailable { get; set; }
  public IReadOnlyList<int> BackendAlternatives { get; set; } = [];
  public IReadOnlyList<int> WebUiAlternatives { get; set; } = [];
  public DateTimeOffset CapturedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
