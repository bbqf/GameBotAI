namespace GameBot.Emulator.Session;

public sealed class SessionOptions {
  /// <summary>
  /// How many emulator sessions may be open at once. Bound from
  /// <c>Service:Sessions:MaxConcurrentSessions</c>.
  /// </summary>
  /// <remarks>
  /// Raised from 3 to 8 in feature 079: with concurrent queue runs the old default was below a
  /// plausible emulator count, so the guard rail became the practical ceiling on how many queues
  /// could run at once. It stays a guard against runaway session creation, not a capacity plan.
  /// </remarks>
  public int MaxConcurrentSessions { get; set; } = 8;
  // Bind-friendly seconds value (tests can override via env Service__Sessions__IdleTimeoutSeconds)
  public int IdleTimeoutSeconds { get; set; } = 1800; // 30 minutes
}
