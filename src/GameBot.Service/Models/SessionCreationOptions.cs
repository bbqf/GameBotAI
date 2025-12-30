namespace GameBot.Service.Models;

#pragma warning disable CA1515 // Options need to be public for DI and tests
public sealed class SessionCreationOptions {
  public int TimeoutSeconds { get; set; } = 30;
}
#pragma warning restore CA1515
