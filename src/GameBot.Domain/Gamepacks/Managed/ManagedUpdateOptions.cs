namespace GameBot.Domain.Gamepacks.Managed;

/// <summary>The fixed limits of a managed gamepack update (feature 148).</summary>
public sealed class ManagedUpdateOptions {
  /// <summary>The time that the flow waits for one queue to stop. The default is 60 seconds. A test can set a smaller value.</summary>
  public TimeSpan StopTimeout { get; init; } = TimeSpan.FromSeconds(60);
}
