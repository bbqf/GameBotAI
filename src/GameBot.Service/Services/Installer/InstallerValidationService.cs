namespace GameBot.Service.Services.Installer;

internal sealed class InstallerValidationService {
  public static IReadOnlyList<string> ValidateModeAndProtocol(string installMode, string protocol) {
    var errors = new List<string>();
    if (!string.Equals(installMode, InstallerDefaults.ServiceMode, StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(installMode, InstallerDefaults.BackgroundAppMode, StringComparison.OrdinalIgnoreCase)) {
      errors.Add("installMode must be 'service' or 'backgroundApp'.");
    }

    if (!string.Equals(protocol, "http", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(protocol, "https", StringComparison.OrdinalIgnoreCase)) {
      errors.Add("protocol must be 'http' or 'https'.");
    }

    return errors;
  }

  public static IReadOnlyList<string> ValidatePorts(int backendPort, int webUiPort) {
    var errors = new List<string>();
    if (backendPort < 1 || backendPort > 65535) {
      errors.Add("backendPort must be between 1 and 65535.");
    }
    if (webUiPort < 1 || webUiPort > 65535) {
      errors.Add("webUiPort must be between 1 and 65535.");
    }
    if (backendPort == webUiPort) {
      errors.Add("backendPort and webUiPort must be different.");
    }
    return errors;
  }
}
