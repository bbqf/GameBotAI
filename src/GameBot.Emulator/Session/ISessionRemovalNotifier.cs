using System;

namespace GameBot.Emulator.Session;

/// <summary>
/// Tells subscribers that a session went out of the session table (feature 166). A session manager
/// raises <see cref="SessionRemoved"/> one time for each removal, for example a stop or the idle sweep.
/// This interface is separate from <see cref="ISessionManager"/>, so the test fakes of
/// <see cref="ISessionManager"/> do not have to implement it.
/// </summary>
public interface ISessionRemovalNotifier {
  /// <summary>Occurs one time after a session goes out of the session table.</summary>
  event EventHandler<SessionRemovedEventArgs>? SessionRemoved;
}

/// <summary>The data of the <see cref="ISessionRemovalNotifier.SessionRemoved"/> event.</summary>
public sealed class SessionRemovedEventArgs : EventArgs {
  /// <summary>Makes the event data for one removed session.</summary>
  /// <param name="sessionId">The id of the removed session.</param>
  public SessionRemovedEventArgs(string sessionId) {
    SessionId = sessionId;
  }

  /// <summary>The id of the removed session.</summary>
  public string SessionId { get; }
}
