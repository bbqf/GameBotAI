using GameBot.Domain.Installer;
using GameBot.Service.Models.Installer;

namespace GameBot.Service.Services.Installer;

internal sealed class InstallExecutionService {
  private readonly IInstallerConfigurationRepository _configRepo;
  private readonly WebUiApiConfigWriter _webUiConfigWriter;
  private readonly PortProbeService _portProbeService;

  public InstallExecutionService(
    IInstallerConfigurationRepository configRepo,
    WebUiApiConfigWriter webUiConfigWriter,
    PortProbeService portProbeService) {
    _configRepo = configRepo;
    _webUiConfigWriter = webUiConfigWriter;
    _portProbeService = portProbeService;
  }

  public async Task<InstallerExecutionResult> ExecuteAsync(InstallerPreflightRequest request, CancellationToken ct = default) {
    var scanned = await PrerequisiteScanner.ScanAsync(ct).ConfigureAwait(false);
    var prerequisites = await PrerequisiteInstaller.EnsureInstalledAsync(scanned, ct).ConfigureAwait(false);

    var selectedWebUiPort = _portProbeService.ResolvePreferredWebUiPort(request.BackendPort, request.RequestedWebUiPort);
    if (selectedWebUiPort == 0) {
      return new InstallerExecutionResult {
        RunId = Guid.NewGuid().ToString("N"),
        Status = InstallationRunStatus.Failed,
        Prerequisites = prerequisites,
        Errors = ["No available Web UI port could be resolved from the preferred order."],
        CompletedAtUtc = DateTimeOffset.UtcNow
      };
    }

    var isServiceMode = string.Equals(request.InstallMode, InstallerDefaults.ServiceMode, StringComparison.OrdinalIgnoreCase);
    var registration = isServiceMode
      ? ServiceModeRegistrar.Evaluate()
      : BackgroundAppRegistrar.Evaluate(request.StartOnLogin);

    var firewall = FirewallPolicyService.Evaluate(isServiceMode, request.ConfirmFirewallFallback);
    if (!firewall.CanProceed) {
      return new InstallerExecutionResult {
        RunId = Guid.NewGuid().ToString("N"),
        Status = InstallationRunStatus.Aborted,
        Prerequisites = prerequisites,
        Warnings = firewall.Warnings,
        Errors = firewall.Errors,
        CompletedAtUtc = DateTimeOffset.UtcNow
      };
    }

    var endpoint = BackendNetworkConfigurator.Configure(request.Protocol, request.BackendPort, selectedWebUiPort, firewall.FirewallScope);

    var profile = new InstallationProfile {
      ProfileId = Guid.NewGuid().ToString("N"),
      InstallMode = request.InstallMode,
      InstallRootPath = AppContext.BaseDirectory,
      Unattended = request.Unattended,
      StartupPolicy = registration.StartupPolicy,
      Protocol = request.Protocol,
      BackendPort = request.BackendPort,
      WebUiPort = selectedWebUiPort,
      BackendHostScope = endpoint.BackendHostScope,
      FirewallScope = endpoint.FirewallScope,
      CreatedAtUtc = DateTimeOffset.UtcNow,
      UpdatedAtUtc = DateTimeOffset.UtcNow
    };

    await _configRepo.SaveAsync(profile, ct).ConfigureAwait(false);

    await _webUiConfigWriter.WriteAsync(endpoint.AnnouncedBackendUrl, ct).ConfigureAwait(false);

    var warnings = new List<string>(firewall.Warnings);
    if (request.RequestedWebUiPort.HasValue && request.RequestedWebUiPort.Value != selectedWebUiPort) {
      warnings.Add($"Requested Web UI port {request.RequestedWebUiPort.Value} was unavailable. Using {selectedWebUiPort}.");
    }

    return new InstallerExecutionResult {
      RunId = Guid.NewGuid().ToString("N"),
      Status = InstallationRunStatus.Success,
      SelectedProfileId = profile.ProfileId,
      EndpointConfiguration = endpoint,
      Prerequisites = prerequisites,
      Warnings = warnings,
      CompletedAtUtc = DateTimeOffset.UtcNow
    };
  }
}
