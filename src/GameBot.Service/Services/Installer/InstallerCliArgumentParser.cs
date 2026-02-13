using GameBot.Service.Models.Installer;

namespace GameBot.Service.Services.Installer;

internal static class InstallerCliArgumentParser {
  public static InstallerCliParseResult Parse(IReadOnlyList<string> args) {
    ArgumentNullException.ThrowIfNull(args);

    var request = new InstallerPreflightRequest {
      InstallMode = InstallerDefaults.BackgroundAppMode,
      BackendPort = InstallerDefaults.DefaultBackendPort,
      RequestedWebUiPort = null,
      Protocol = InstallerDefaults.DefaultProtocol,
      Unattended = true,
      StartOnLogin = true,
      ConfirmFirewallFallback = true
    };

    var errors = new List<string>();
    var showHelp = false;

    for (var index = 0; index < args.Count; index++) {
      var token = args[index];
      switch (token.ToLowerInvariant()) {
        case "--help":
        case "-h":
        case "/?":
          showHelp = true;
          break;
        case "--mode":
        case "-m":
          if (!TryGetNextValue(args, ref index, out var modeValue)) {
            errors.Add("Missing value for --mode.");
            break;
          }

          request.InstallMode = modeValue;
          break;
        case "--backend-port":
          if (!TryReadInt(args, ref index, "--backend-port", out var backendPort, out var backendError)) {
            errors.Add(backendError);
            break;
          }

          request.BackendPort = backendPort;
          break;
        case "--web-port":
          if (!TryReadInt(args, ref index, "--web-port", out var webPort, out var webError)) {
            errors.Add(webError);
            break;
          }

          request.RequestedWebUiPort = webPort;
          break;
        case "--protocol":
          if (!TryGetNextValue(args, ref index, out var protocolValue)) {
            errors.Add("Missing value for --protocol.");
            break;
          }

          request.Protocol = protocolValue;
          break;
        case "--start-on-login":
          request.StartOnLogin = true;
          break;
        case "--no-start-on-login":
          request.StartOnLogin = false;
          break;
        case "--confirm-firewall-fallback":
          if (!TryGetNextValue(args, ref index, out var fallbackValue) || !bool.TryParse(fallbackValue, out var parsedFallback)) {
            errors.Add("--confirm-firewall-fallback requires true or false.");
            break;
          }

          request.ConfirmFirewallFallback = parsedFallback;
          break;
        case "--unattended":
          request.Unattended = true;
          break;
        default:
          errors.Add($"Unknown argument '{token}'.");
          break;
      }
    }

    errors.AddRange(InstallerValidationService.ValidateModeAndProtocol(request.InstallMode, request.Protocol));
    if (request.RequestedWebUiPort.HasValue) {
      errors.AddRange(InstallerValidationService.ValidatePorts(request.BackendPort, request.RequestedWebUiPort.Value));
    }
    else {
      errors.AddRange(InstallerValidationService.ValidatePorts(request.BackendPort, InstallerDefaults.PreferredWebPorts[0]));
    }

    return new InstallerCliParseResult {
      ShowHelp = showHelp,
      Request = request,
      Errors = [.. errors]
    };
  }

  private static bool TryGetNextValue(IReadOnlyList<string> args, ref int index, out string value) {
    var nextIndex = index + 1;
    if (nextIndex >= args.Count) {
      value = string.Empty;
      return false;
    }

    value = args[nextIndex];
    index = nextIndex;
    return true;
  }

  private static bool TryReadInt(IReadOnlyList<string> args, ref int index, string optionName, out int value, out string error) {
    value = 0;
    error = string.Empty;
    if (!TryGetNextValue(args, ref index, out var textValue)) {
      error = $"Missing value for {optionName}.";
      return false;
    }

    if (!int.TryParse(textValue, out value)) {
      error = $"{optionName} must be an integer.";
      return false;
    }

    return true;
  }
}

internal sealed class InstallerCliParseResult {
  public bool ShowHelp { get; set; }
  public InstallerPreflightRequest? Request { get; set; }
  public IReadOnlyList<string> Errors { get; set; } = [];
  public bool IsValid => Errors.Count == 0;
}
