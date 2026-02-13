namespace GameBot.Service.Services.Installer;

internal static class InstallerCliErrorFormatter {
  public static string FormatUsage() {
    return "Usage: install-gamebot.ps1 --mode <service|backgroundApp> --backend-port <1-65535> [--web-port <1-65535>] --protocol <http|https> [--start-on-login|--no-start-on-login] [--confirm-firewall-fallback <true|false>] --unattended";
  }

  public static string FormatValidationErrors(IReadOnlyList<string> errors) {
    if (errors.Count == 0) {
      return string.Empty;
    }

    var details = string.Join(Environment.NewLine, errors.Select(error => $"- {error}"));
    return $"Validation failed:{Environment.NewLine}{details}{Environment.NewLine}Remediation: review arguments and rerun with --help.";
  }
}
