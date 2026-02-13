namespace GameBot.Service.Services.Installer;

internal sealed class BackgroundAppRegistrar {
  public static RegistrationDecision Evaluate(bool startOnLogin) {
    return new RegistrationDecision {
      RequiresElevation = false,
      StartupPolicy = startOnLogin ? "loginStartWhenEnabled" : "manual"
    };
  }
}
