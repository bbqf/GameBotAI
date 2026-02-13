using System.Collections.Concurrent;
using GameBot.Domain.Installer;
using GameBot.Service.Models.Installer;
using GameBot.Service.Services.Installer;

namespace GameBot.Service.Endpoints;

internal static class InstallerEndpoints {
  private static readonly ConcurrentDictionary<string, InstallerExecutionResult> Runs = new(StringComparer.Ordinal);

  public static IEndpointRouteBuilder MapInstallerEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Installer).WithTags("Installer");

    group.MapPost("/preflight", (InstallerPreflightRequest req, PortProbeService portProbeService) => {
      var selectedWebPort = portProbeService.ResolvePreferredWebUiPort(req.BackendPort, req.RequestedWebUiPort);
      var validationWebPort = selectedWebPort == 0
        ? (req.RequestedWebUiPort ?? InstallerDefaults.PreferredWebPorts.First())
        : selectedWebPort;

      var errors = InstallerValidationService.ValidateModeAndProtocol(req.InstallMode, req.Protocol)
        .Concat(InstallerValidationService.ValidatePorts(req.BackendPort, validationWebPort))
        .ToArray();

      if (selectedWebPort == 0) {
        errors = [.. errors, "No available Web UI port could be resolved from the preferred order."];
      }

      if (errors.Length > 0) {
        return Results.BadRequest(new { errors });
      }

      var isServiceMode = string.Equals(req.InstallMode, InstallerDefaults.ServiceMode, StringComparison.OrdinalIgnoreCase);
      var registration = isServiceMode
        ? ServiceModeRegistrar.Evaluate()
        : BackgroundAppRegistrar.Evaluate(req.StartOnLogin);

      var firewall = FirewallPolicyService.Evaluate(isServiceMode, req.ConfirmFirewallFallback);
      if (!firewall.CanProceed) {
        return Results.BadRequest(new { errors = firewall.Errors, warnings = firewall.Warnings });
      }

      var requestedWebPort = req.RequestedWebUiPort ?? selectedWebPort;
      var probe = portProbeService.Probe(req.BackendPort, requestedWebPort);
      var warnings = new List<string>(firewall.Warnings);
      if (req.RequestedWebUiPort.HasValue && req.RequestedWebUiPort.Value != selectedWebPort) {
        warnings.Add($"Requested Web UI port {req.RequestedWebUiPort.Value} was unavailable. Using {selectedWebPort}.");
      }

      var response = new InstallerPreflightResponse {
        CanProceed = true,
        RequiresElevation = registration.RequiresElevation,
        Warnings = warnings,
        SelectedWebUiPort = selectedWebPort,
        SuggestedWebUiPorts = probe.WebUiAlternatives.Count > 0
          ? [selectedWebPort, .. probe.WebUiAlternatives]
          : [selectedWebPort],
        StartupPolicy = registration.StartupPolicy,
        FirewallScope = firewall.FirewallScope,
        PortProbe = probe
      };

      return Results.Ok(response);
    }).WithName("InstallerPreflight");

    group.MapPost("/execute", async (InstallerPreflightRequest req, InstallExecutionService installExecutionService, PortProbeService portProbeService, CancellationToken ct) => {
      var selectedWebPort = portProbeService.ResolvePreferredWebUiPort(req.BackendPort, req.RequestedWebUiPort);
      var validationWebPort = selectedWebPort == 0
        ? (req.RequestedWebUiPort ?? InstallerDefaults.PreferredWebPorts.First())
        : selectedWebPort;

      var errors = InstallerValidationService.ValidateModeAndProtocol(req.InstallMode, req.Protocol)
        .Concat(InstallerValidationService.ValidatePorts(req.BackendPort, validationWebPort))
        .ToArray();

      if (selectedWebPort == 0) {
        errors = [.. errors, "No available Web UI port could be resolved from the preferred order."];
      }

      if (errors.Length > 0) {
        return Results.BadRequest(new { errors });
      }

      var result = await installExecutionService.ExecuteAsync(req, ct).ConfigureAwait(false);

      Runs[result.RunId] = result;
      return Results.Ok(result);
    }).WithName("InstallerExecute");

    group.MapGet("/status/{runId}", (string runId) => {
      return Runs.TryGetValue(runId, out var run)
        ? Results.Ok(run)
        : Results.NotFound(new { error = "installer_run_not_found", runId });
    }).WithName("InstallerStatus");

    return app;
  }
}
