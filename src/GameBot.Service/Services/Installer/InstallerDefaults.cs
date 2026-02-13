namespace GameBot.Service.Services.Installer;

internal static class InstallerDefaults {
  internal static readonly int[] PreferredWebPorts = [8080, 8088, 8888, 80];
  internal const int DefaultBackendPort = 5000;
  internal const string DefaultProtocol = "http";
  internal const string ServiceMode = "service";
  internal const string BackgroundAppMode = "backgroundApp";
}
