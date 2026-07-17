namespace GameBot.Domain.Actions;

public sealed class PrimitiveTapAction : PrimitiveActionBase {
  public int? X { get; set; }
  public int? Y { get; set; }
  public int? DelayMs { get; set; }
  public int? DurationMs { get; set; }

  public PrimitiveTapAction() : base(PrimitiveActionTypes.Tap) { }
}

public sealed class PrimitiveSwipeAction : PrimitiveActionBase {
  public int? X1 { get; set; }
  public int? Y1 { get; set; }
  public int? X2 { get; set; }
  public int? Y2 { get; set; }
  public int? DurationMs { get; set; }

  public PrimitiveSwipeAction() : base(PrimitiveActionTypes.Swipe) { }
}

public sealed class PrimitiveKeyAction : PrimitiveActionBase {
  public string? Key { get; set; }
  public int? KeyCode { get; set; }

  public PrimitiveKeyAction() : base(PrimitiveActionTypes.Key) { }
}

public sealed class PrimitiveCommandAction : PrimitiveActionBase {
  public string? CommandId { get; set; }

  public PrimitiveCommandAction() : base(PrimitiveActionTypes.Command) { }
}

public sealed class PrimitiveEnsureGameRunningAction : PrimitiveActionBase {
  public PrimitiveEnsureGameRunningAction() : base(PrimitiveActionTypes.EnsureGameRunning) { }
}

/// <summary>
/// Feature 069: parameterless action that presses the Android HOME button so the device returns to
/// its home/main screen, leaving the game running in the background.
/// </summary>
public sealed class PrimitiveGoToHomeScreenAction : PrimitiveActionBase {
  public PrimitiveGoToHomeScreenAction() : base(PrimitiveActionTypes.GoToHomeScreen) { }
}

/// <summary>
/// Feature 070: ensures the target LDPlayer emulator instance is running and responsive, starting or
/// restarting it when it is not. Parameterized (like <see cref="PrimitiveConnectToGameAction"/>): the
/// author supplies an instance identifier (name or index) plus the adbSerial used for the probe.
/// </summary>
public sealed class PrimitiveEnsureEmulatorRunningAction : PrimitiveActionBase {
  public string? InstanceName { get; set; }
  public int? InstanceIndex { get; set; }
  public string? AdbSerial { get; set; }

  public PrimitiveEnsureEmulatorRunningAction() : base(PrimitiveActionTypes.EnsureEmulatorRunning) { }

  public EnsureEmulatorRunningArgs? ToEnsureEmulatorRunningArgs() =>
    EnsureEmulatorRunningArgs.TryFrom(this, out var args) ? args : null;
}

public sealed class PrimitiveConnectToGameAction : PrimitiveActionBase {
  public string? GameId { get; set; }
  public string? AdbSerial { get; set; }

  public PrimitiveConnectToGameAction() : base(PrimitiveActionTypes.ConnectToGame) { }

  public ConnectToGameArgs? ToConnectToGameArgs() {
    if (string.IsNullOrWhiteSpace(GameId) || string.IsNullOrWhiteSpace(AdbSerial)) {
      return null;
    }

    return new ConnectToGameArgs {
      GameId = GameId,
      AdbSerial = AdbSerial
    };
  }
}
