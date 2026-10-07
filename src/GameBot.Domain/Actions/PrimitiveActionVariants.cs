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

  /// <summary>
  /// Feature 129: when true, the step stops the game, starts it again, and waits for the foreground.
  /// Null and false both mean the plain check. Null is not written to storage.
  /// </summary>
  [System.Text.Json.Serialization.JsonIgnore(
      Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
  public bool? ForceRestart { get; set; }

  /// <summary>
  /// Makes the stored payload of this action. The <c>forceRestart</c> key exists only when the value
  /// is true or false.
  /// </summary>
  public GameBot.Domain.Commands.SequenceActionPayload ToActionPayload() {
    var payload = new GameBot.Domain.Commands.SequenceActionPayload {
      Type = Type,
      SchemaVersion = SchemaVersion
    };
    if (ForceRestart is { } value) {
      payload.Parameters[GameBot.Domain.Commands.EnsureGameRunning.EnsureGameRunningPayload.ForceRestartKey] = value;
    }
    return payload;
  }

  /// <summary>Reads a typed action from a stored payload. Returns false with an error for a bad value.</summary>
  public static bool TryFromActionPayload(
      GameBot.Domain.Commands.SequenceActionPayload? payload,
      out PrimitiveEnsureGameRunningAction? action,
      out string? error) {
    action = null;
    if (payload is null) {
      error = "action payload is missing";
      return false;
    }
    if (!GameBot.Domain.Commands.EnsureGameRunning.EnsureGameRunningPayload.TryRead(payload, out var forceRestart, out error)) {
      return false;
    }
    var hasKey = payload.Parameters.ContainsKey(GameBot.Domain.Commands.EnsureGameRunning.EnsureGameRunningPayload.ForceRestartKey);
    action = new PrimitiveEnsureGameRunningAction {
      SchemaVersion = payload.SchemaVersion,
      ForceRestart = hasKey ? forceRestart : null
    };
    return true;
  }
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

  /// <summary>
  /// Optional LDPlayer instance identifier (feature 071). When supplied (name or index), connect-to-game
  /// first ensures that emulator instance is running/responsive before attaching the session.
  /// </summary>
  public string? InstanceName { get; set; }
  public int? InstanceIndex { get; set; }

  public PrimitiveConnectToGameAction() : base(PrimitiveActionTypes.ConnectToGame) { }

  public ConnectToGameArgs? ToConnectToGameArgs() {
    if (string.IsNullOrWhiteSpace(GameId) || string.IsNullOrWhiteSpace(AdbSerial)) {
      return null;
    }

    return new ConnectToGameArgs {
      GameId = GameId,
      AdbSerial = AdbSerial,
      InstanceName = InstanceName,
      InstanceIndex = InstanceIndex
    };
  }
}
