using GameBot.Service.Models.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class InstallerCliCoordinator {
  private readonly Func<InstallerPreflightRequest, CancellationToken, Task<InstallerCliExecutionResponse>> _executeRequest;

  public InstallerCliCoordinator(
    Func<InstallerPreflightRequest, CancellationToken, Task<InstallerCliExecutionResponse>> executeRequest) {
    _executeRequest = executeRequest;
  }

  public async Task<InstallerCliExecutionResult> ExecuteAsync(IReadOnlyList<string> args, CancellationToken ct = default) {
    var parsed = InstallerCliArgumentParser.Parse(args);
    if (parsed.ShowHelp) {
      return new InstallerCliExecutionResult {
        ExitCode = 0,
        Message = InstallerCliErrorFormatter.FormatUsage()
      };
    }

    if (!parsed.IsValid || parsed.Request is null) {
      return new InstallerCliExecutionResult {
        ExitCode = 2,
        Message = InstallerCliErrorFormatter.FormatValidationErrors(parsed.Errors)
      };
    }

    var executeResponse = await _executeRequest(parsed.Request, ct).ConfigureAwait(false);
    return new InstallerCliExecutionResult {
      ExitCode = executeResponse.Success ? 0 : 3,
      Message = executeResponse.Message
    };
  }
}

internal sealed class InstallerCliExecutionResponse {
  public bool Success { get; set; }
  public string Message { get; set; } = string.Empty;
}

internal sealed class InstallerCliExecutionResult {
  public int ExitCode { get; set; }
  public string Message { get; set; } = string.Empty;
}
