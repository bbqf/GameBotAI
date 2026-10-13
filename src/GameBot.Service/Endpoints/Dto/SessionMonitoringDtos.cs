namespace GameBot.Service.Endpoints.Dto;

/// <summary>Feature 141: the state values of a monitored session.</summary>
internal static class MonitoredSessionStates {
  public const string Live = "live";
  public const string NoAnswer = "no_answer";
  public const string Restarting = "restarting";
  public const string Stopping = "stopping";

  public static readonly string[] All = [Live, NoAnswer, Restarting, Stopping];
}

/// <summary>Feature 141: the state values of a restart record.</summary>
internal static class RestartStates {
  public const string Restarting = "restarting";
  public const string Success = "success";
  public const string Failure = "failure";

  public static readonly string[] All = [Restarting, Success, Failure];
}

/// <summary>Feature 141: the step values of a restart record.</summary>
internal static class RestartSteps {
  public const string Starting = "starting";
  public const string Rebooting = "rebooting";
  public const string WaitingForDevice = "waiting_for_device";
  public const string Reconnecting = "reconnecting";
  public const string CheckingCapture = "checking_capture";
  public const string Done = "done";

  public static readonly string[] All = [Starting, Rebooting, WaitingForDevice, Reconnecting, CheckingCapture, Done];
}

/// <summary>Feature 141: the failure reasons of a restart record.</summary>
internal static class RestartReasons {
  public const string Timeout = "timeout";
  public const string InstanceNotFound = "instance_not_found";
  public const string RebootFailed = "reboot_failed";
  public const string ReconnectFailed = "reconnect_failed";
  public const string ControlUnavailable = "control_unavailable";
  public const string Cancelled = "cancelled";
  public const string InternalError = "internal_error";

  public static readonly string[] All =
    [Timeout, InstanceNotFound, RebootFailed, ReconnectFailed, ControlUnavailable, Cancelled, InternalError];
}

/// <summary>Feature 141: the reasons why a restart is not available.</summary>
internal static class RestartUnavailableReasons {
  public const string NoDeviceSerial = "no_device_serial";
  public const string UnsupportedSerial = "unsupported_serial";
  public const string LdConsoleMissing = "ldconsole_missing";
  public const string PlatformUnsupported = "platform_unsupported";
  public const string InstanceNotFound = "instance_not_found";

  public static readonly string[] All =
    [NoDeviceSerial, UnsupportedSerial, LdConsoleMissing, PlatformUnsupported, InstanceNotFound];
}

/// <summary>Feature 141: one session in the monitoring list.</summary>
internal sealed class MonitoredSessionDto {
  public string Id { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public string GameId { get; set; } = string.Empty;
  public string? DeviceSerial { get; set; }
  public string State { get; set; } = MonitoredSessionStates.Live;
  public string? Reason { get; set; }
  public string? OwnerQueueId { get; set; }
  public string? OwnerQueueName { get; set; }
  public string? CurrentSequenceName { get; set; }
  public bool RestartAvailable { get; set; }
  public string? RestartUnavailableReason { get; set; }
  public DateTimeOffset StartedAtUtc { get; set; }
}

/// <summary>Feature 141: the answer of <c>GET /api/sessions/monitoring</c>.</summary>
internal sealed class SessionMonitoringListResponse {
  public IReadOnlyList<MonitoredSessionDto> Sessions { get; set; } = [];
}

/// <summary>Feature 141: the last restart record of a session.</summary>
internal sealed class SessionRestartRecordDto {
  public string SessionId { get; set; } = string.Empty;
  public string State { get; set; } = RestartStates.Restarting;
  public string Step { get; set; } = RestartSteps.Starting;
  public DateTimeOffset StartedAtUtc { get; set; }
  public DateTimeOffset? EndedAtUtc { get; set; }
  public string? Reason { get; set; }
}
