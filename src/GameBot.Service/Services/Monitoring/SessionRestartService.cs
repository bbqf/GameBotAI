using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using GameBot.Service.Endpoints.Dto;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Liveness;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.Monitoring;

/// <summary>Feature 141: tells if the system can restart the emulator of a device serial.</summary>
/// <param name="Available">True when a restart is possible.</param>
/// <param name="Reason">A value of <see cref="RestartUnavailableReasons"/> when not available.</param>
/// <param name="InstanceIndex">The LDPlayer instance index when available, else -1.</param>
internal readonly record struct RestartAvailability(bool Available, string? Reason, int InstanceIndex);

internal enum RestartStartStatus { Accepted, InProgress, Unavailable }

/// <summary>The answer of <see cref="ISessionRestartService.StartAsync"/>.</summary>
/// <param name="Status">Accepted, refused because a restart runs, or refused because the system cannot restart.</param>
/// <param name="Record">The new record (Accepted) or the running record (InProgress).</param>
/// <param name="UnavailableReason">Set when the status is Unavailable.</param>
/// <param name="Completion">Ends when the background restart ends. Tests wait on it.</param>
internal sealed record RestartStartResult(RestartStartStatus Status, SessionRestartRecord? Record, string? UnavailableReason, Task Completion);

/// <summary>Feature 141: restarts the whole emulator of a session and keeps the last record of each session.</summary>
internal interface ISessionRestartService {
  /// <summary>Tells if a restart is possible for the serial. It reads the serial only.</summary>
  RestartAvailability GetAvailability(string? deviceSerial);

  /// <summary>
  /// Starts a restart in the background. It answers at once. The restart does not use the request token, so
  /// a closed browser does not stop it.
  /// </summary>
  Task<RestartStartResult> StartAsync(EmulatorSession session, CancellationToken ct);

  /// <summary>Returns the last record of the session, or null.</summary>
  SessionRestartRecord? Get(string sessionId);

  /// <summary>Drops the records of sessions that no longer exist.</summary>
  void Prune(IReadOnlyCollection<string> liveSessionIds);
}

/// <summary>
/// Feature 141 (FR-013 to FR-017): the restart. Steps: reboot the instance with ldconsole, wait until the
/// device answers, reconnect the session (same session id), check one capture. The whole run ends in at most
/// <see cref="SessionMonitoringOptions.RestartTimeoutMs"/>. The instance index comes only from the serial of
/// the session, so a restart never touches another emulator (SC-007).
/// </summary>
internal sealed class SessionRestartService : ISessionRestartService {
  private readonly IEmulatorControl _control;
  private readonly IEmulatorDeviceProbe _probe;
  private readonly ISessionReconnector _reconnector;
  private readonly ISessionDirectCapture _capture;
  private readonly SessionMonitoringOptions _options;
  private readonly TimeProvider _clock;
  private readonly ILogger _logger;
  private readonly SessionRestartStore _store = new();

  public SessionRestartService(
      IEmulatorControl control,
      IEmulatorDeviceProbe probe,
      ISessionReconnector reconnector,
      ISessionDirectCapture capture,
      IOptions<SessionMonitoringOptions> options,
      TimeProvider clock,
      ILogger<MonitoringLoggingTag> logger) {
    _control = control;
    _probe = probe;
    _reconnector = reconnector;
    _capture = capture;
    _options = (options?.Value ?? new SessionMonitoringOptions()).Normalized();
    _clock = clock;
    _logger = logger;
  }

  public RestartAvailability GetAvailability(string? deviceSerial) {
    if (!LdInstanceMapper.TryGetIndex(deviceSerial, out var index, out var reason)) {
      return new RestartAvailability(false, reason, -1);
    }

    if (!OperatingSystem.IsWindows()) return new RestartAvailability(false, RestartUnavailableReasons.PlatformUnsupported, -1);
    if (!_control.IsAvailable) return new RestartAvailability(false, RestartUnavailableReasons.LdConsoleMissing, -1);
    return new RestartAvailability(true, null, index);
  }

  public SessionRestartRecord? Get(string sessionId) => _store.Get(sessionId);

  public void Prune(IReadOnlyCollection<string> liveSessionIds) => _store.Prune(liveSessionIds);

  public async Task<RestartStartResult> StartAsync(EmulatorSession session, CancellationToken ct) {
    ArgumentNullException.ThrowIfNull(session);
    var serial = session.DeviceSerial ?? string.Empty;

    // A second request while a restart runs is refused before anything else (FR-013).
    if (_store.Get(session.Id) is { IsRunning: true } running) {
      MonitoringLog.RestartRefused(_logger, session.Id, "restart_in_progress");
      return new RestartStartResult(RestartStartStatus.InProgress, running, null, Task.CompletedTask);
    }

    var availability = GetAvailability(session.DeviceSerial);
    if (!availability.Available) return Unavailable(session.Id, availability.Reason);

    var state = await _control.GetRunStateAsync(null, availability.InstanceIndex, ct).ConfigureAwait(false);
    if (state != LdConsoleRunState.Running) return Unavailable(session.Id, RestartUnavailableReasons.InstanceNotFound);

    if (!_store.TryBegin(session.Id, _clock.GetUtcNow(), out var record)) {
      MonitoringLog.RestartRefused(_logger, session.Id, "restart_in_progress");
      return new RestartStartResult(RestartStartStatus.InProgress, record, null, Task.CompletedTask);
    }

    MonitoringLog.RestartRequested(_logger, session.Id, serial);
    var completion = Task.Run(() => RunAsync(session.Id, serial, availability.InstanceIndex), CancellationToken.None);
    return new RestartStartResult(RestartStartStatus.Accepted, record, null, completion);
  }

  private RestartStartResult Unavailable(string sessionId, string? reason) {
    var text = reason ?? "unknown";
    MonitoringLog.RestartRefused(_logger, sessionId, text);
    return new RestartStartResult(RestartStartStatus.Unavailable, null, text, Task.CompletedTask);
  }

  private async Task RunAsync(string sessionId, string serial, int instanceIndex) {
    using var limit = new CancellationTokenSource(TimeSpan.FromMilliseconds(_options.RestartTimeoutMs), _clock);
    try {
      var failure = await RunStepsAsync(sessionId, serial, instanceIndex, limit.Token).ConfigureAwait(false);
      if (failure is null) Succeed(sessionId);
      else Fail(sessionId, failure);
    }
    catch (OperationCanceledException) when (limit.IsCancellationRequested) {
      Fail(sessionId, RestartReasons.Timeout);
    }
    catch (OperationCanceledException) {
      Fail(sessionId, RestartReasons.Cancelled);
    }
    catch (Exception) {
      Fail(sessionId, RestartReasons.InternalError);
    }
  }

  /// <summary>Runs the steps in order. Returns null on success, else the failure reason.</summary>
  private async Task<string?> RunStepsAsync(string sessionId, string serial, int instanceIndex, CancellationToken ct) {
    SetStep(sessionId, RestartSteps.Rebooting);
    var rebooted = await _control.RebootAsync(null, instanceIndex, ct).ConfigureAwait(false);
    if (!rebooted) return RestartReasons.RebootFailed;

    SetStep(sessionId, RestartSteps.WaitingForDevice);
    await WaitForDeviceAsync(serial, ct).ConfigureAwait(false);

    SetStep(sessionId, RestartSteps.Reconnecting);
    try {
      await _reconnector.ReconnectAsync(sessionId, serial, ct).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is not OperationCanceledException) {
      return RestartReasons.ReconnectFailed;
    }

    SetStep(sessionId, RestartSteps.CheckingCapture);
    await WaitForCaptureAsync(serial, ct).ConfigureAwait(false);
    return null;
  }

  private async Task WaitForDeviceAsync(string serial, CancellationToken ct) {
    var delay = TimeSpan.FromMilliseconds(_options.RestartPollIntervalMs);
    do {
      // The wait comes first: right after the reboot command the old device state can still answer.
      await Task.Delay(delay, _clock, ct).ConfigureAwait(false);
    }
    while (!await _probe.IsResponsiveAsync(serial, ct).ConfigureAwait(false));
  }

  private async Task WaitForCaptureAsync(string serial, CancellationToken ct) {
    var delay = TimeSpan.FromMilliseconds(_options.RestartPollIntervalMs);
    while (!await _capture.TryCaptureAsync(serial, ct).ConfigureAwait(false)) {
      await Task.Delay(delay, _clock, ct).ConfigureAwait(false);
    }
  }

  private void SetStep(string sessionId, string step) {
    _store.SetStep(sessionId, step);
    MonitoringLog.RestartStep(_logger, sessionId, step);
  }

  private void Succeed(string sessionId) {
    _store.End(sessionId, RestartStates.Success, RestartSteps.Done, null, _clock.GetUtcNow());
    MonitoringLog.RestartSucceeded(_logger, sessionId, RestartStates.Success);
  }

  private void Fail(string sessionId, string reason) {
    _store.End(sessionId, RestartStates.Failure, _store.Get(sessionId)?.Step ?? RestartSteps.Starting, reason, _clock.GetUtcNow());
    MonitoringLog.RestartFailed(_logger, sessionId, reason);
  }
}
