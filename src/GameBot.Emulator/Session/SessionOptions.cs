namespace GameBot.Emulator.Session;

public sealed class SessionOptions {
  public int MaxConcurrentSessions { get; set; } = 3;
  // Bind-friendly seconds value (tests can override via env Service__Sessions__IdleTimeoutSeconds)
  public int IdleTimeoutSeconds { get; set; } = 1800; // 30 minutes
}
