using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameBot.Emulator.Session;

namespace GameBot.Service.Services;

/// <summary>
/// The single rule for deciding which emulator session a step acts on, shared by the sequence
/// dispatcher and the command executor (feature 079, FR-006/FR-007).
/// </summary>
/// <remarks>
/// Before feature 079 each call site inlined its own "exactly one running session" check and failed
/// whenever the count was not one — so starting a second queue broke steps in the first, even though
/// those steps were given an explicit session. The rule now is:
/// <list type="number">
///   <item>an explicit session always wins, however many are running;</item>
///   <item>with none supplied and exactly one running, use it (unchanged single-emulator behaviour);</item>
///   <item>with none supplied and none running, ask the operator to start one;</item>
///   <item>with none supplied and several running, fail naming the count — never guess a device.</item>
/// </list>
/// </remarks>
internal static class SessionResolver {
  /// <summary>
  /// Resolves the session a step must act on.
  /// </summary>
  /// <param name="sessions">Session manager to enumerate running sessions from.</param>
  /// <param name="sessionId">Session from the execution context; null/blank when there is none.</param>
  /// <param name="stepType">Step or command name, used in the failure message.</param>
  /// <param name="resolved">The resolved session id when this returns true; otherwise null.</param>
  /// <param name="error">An actionable failure message when this returns false; otherwise empty.</param>
  /// <returns>True when a session was unambiguously resolved.</returns>
  internal static bool TryResolve(
      ISessionManager sessions,
      string? sessionId,
      string stepType,
      out string? resolved,
      out string error) {
    if (!string.IsNullOrWhiteSpace(sessionId)) {
      resolved = sessionId;
      error = string.Empty;
      return true;
    }

    var running = RunningSessionCount(sessions, out var onlySessionId);
    if (running == 1) {
      resolved = onlySessionId;
      error = string.Empty;
      return true;
    }

    resolved = null;
    error = running == 0 ? NoSession(stepType) : Ambiguous(running, stepType);
    return false;
  }

  /// <summary>Failure text when nothing is running at all (wording unchanged since before 079).</summary>
  internal static string NoSession(string stepType) =>
    string.Format(
      CultureInfo.InvariantCulture,
      "no session available for '{0}' step; start a session or pass a sessionId",
      stepType);

  /// <summary>
  /// Failure text for a step that supplied no session while several device sessions are active.
  /// </summary>
  /// <param name="activeSessionCount">How many device sessions are running (always &gt; 1).</param>
  /// <param name="stepType">The step or command the message is about.</param>
  internal static string Ambiguous(int activeSessionCount, string stepType) =>
    string.Format(
      CultureInfo.InvariantCulture,
      "{0} device sessions are active; specify a sessionId for '{1}'",
      activeSessionCount,
      stepType);

  /// <summary>Counts running sessions, yielding the id when there is exactly one.</summary>
  private static int RunningSessionCount(ISessionManager sessions, out string? onlySessionId) {
    var running = sessions.ListSessions()
      .Where(s => s.Status == GameBot.Domain.Sessions.SessionStatus.Running)
      .ToList();
    onlySessionId = running.Count == 1 ? running[0].Id : null;
    return running.Count;
  }
}
