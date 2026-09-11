using GameBot.Domain.Sessions;

namespace GameBot.Emulator.Session;

public interface ISessionManager {
  int ActiveCount { get; }
  bool CanCreateSession { get; }
  EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null);
  EmulatorSession? GetSession(string id);
  IReadOnlyCollection<EmulatorSession> ListSessions();
  bool StopSession(string id);
  Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default);

  /// <summary>
  /// Same dispatch as <see cref="SendInputsAsync"/>, but reports a per-action outcome instead of
  /// only a count — used by the ad-hoc session-input route (B-001) to distinguish "this session
  /// isn't running" from "these action(s) couldn't be parsed/dispatched," which a bare count
  /// cannot. Does not change <see cref="SendInputsAsync"/>'s own behavior or contract.
  /// </summary>
  Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default);

  Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default);
}

public sealed record InputAction(string Type, Dictionary<string, object> Args, int? DelayMs = null, int? DurationMs = null);

/// <summary>Result of <see cref="ISessionManager.SendInputsWithResultsAsync"/>.</summary>
/// <param name="SessionFound">Whether <c>id</c> resolved to a tracked session at all.</param>
/// <param name="Results">One entry per posted action, in request order.</param>
public sealed record SessionInputDispatchResult(bool SessionFound, IReadOnlyList<InputActionResult> Results);

/// <param name="Index">Position of this action in the request's actions array.</param>
/// <param name="Dispatched">Whether this action reached the device/emulator layer.</param>
/// <param name="FailureReason">Set only when <see cref="Dispatched"/> is false; a short, stable
/// reason — never a raw exception message or stack trace.</param>
public sealed record InputActionResult(int Index, bool Dispatched, string? FailureReason);
