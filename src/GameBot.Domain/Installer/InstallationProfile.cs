namespace GameBot.Domain.Installer;

public sealed class InstallationProfile {
  public string ProfileId { get; set; } = string.Empty;
  public string InstallMode { get; set; } = "backgroundApp";
  public string InstallRootPath { get; set; } = string.Empty;
  public bool Unattended { get; set; }
  public string StartupPolicy { get; set; } = "loginStartWhenEnabled";
  public string Protocol { get; set; } = "http";
  public int BackendPort { get; set; } = 5000;
  public int WebUiPort { get; set; } = 8080;
  public string BackendHostScope { get; set; } = "allInterfaces";
  public string FirewallScope { get; set; } = "privateNetworkOnly";
  public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
  public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}
