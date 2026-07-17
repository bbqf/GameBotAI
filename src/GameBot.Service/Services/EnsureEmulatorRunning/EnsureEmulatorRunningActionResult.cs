namespace GameBot.Service.Services.EnsureEmulatorRunning;

internal enum EnsureEmulatorRunningOutcome {
  /// <summary>Instance was already running and responsive; nothing was done — success.</summary>
  AlreadyHealthy,
  /// <summary>Instance was stopped; it was launched and reached boot-complete — success.</summary>
  Started,
  /// <summary>Instance was hung; it was restarted and reached boot-complete — success.</summary>
  Restarted,
  /// <summary>Instance was (re)started but never became healthy within the boot wait — failure.</summary>
  RecoveryTimedOut,
  /// <summary>The supplied instance identifier matched no instance — failure (FR-014).</summary>
  InstanceNotFound,
  /// <summary>Emulator control is not supported on this host (non-Windows) — neutral no-op.</summary>
  PlatformUnsupported,
  /// <summary>ldconsole and/or ADB could not be located/used — neutral no-op.</summary>
  ControlUnavailable
}

internal sealed record EnsureEmulatorRunningActionResult(EnsureEmulatorRunningOutcome Outcome) {
  /// <summary>Outcomes that leave the emulator healthy — the step succeeds.</summary>
  public bool IsSuccess => Outcome is EnsureEmulatorRunningOutcome.AlreadyHealthy
      or EnsureEmulatorRunningOutcome.Started
      or EnsureEmulatorRunningOutcome.Restarted;

  /// <summary>Neutral "not-applied" outcomes that do not crash the run (mirror ensure-game-running).</summary>
  public bool IsUnsupported => Outcome is EnsureEmulatorRunningOutcome.PlatformUnsupported
      or EnsureEmulatorRunningOutcome.ControlUnavailable;

  public string ReasonCode => Outcome switch {
    EnsureEmulatorRunningOutcome.AlreadyHealthy      => "already_healthy",
    EnsureEmulatorRunningOutcome.Started             => "started",
    EnsureEmulatorRunningOutcome.Restarted           => "restarted",
    EnsureEmulatorRunningOutcome.RecoveryTimedOut    => "recovery_timed_out",
    EnsureEmulatorRunningOutcome.InstanceNotFound    => "instance_not_found",
    EnsureEmulatorRunningOutcome.PlatformUnsupported => "platform_unsupported",
    _                                                => "control_unavailable"
  };

  /// <summary>Human-readable message surfaced on the step (see contract table).</summary>
  public string Message => Outcome switch {
    EnsureEmulatorRunningOutcome.AlreadyHealthy      => "emulator already running and responsive",
    EnsureEmulatorRunningOutcome.Started             => "emulator was started and is responsive",
    EnsureEmulatorRunningOutcome.Restarted           => "emulator was restarted and is responsive",
    EnsureEmulatorRunningOutcome.RecoveryTimedOut    => "recovery timed out",
    EnsureEmulatorRunningOutcome.InstanceNotFound    => "instance not found",
    EnsureEmulatorRunningOutcome.PlatformUnsupported => "emulator control unsupported on this host",
    _                                                => "emulator control unavailable (ldconsole/adb)"
  };
}
