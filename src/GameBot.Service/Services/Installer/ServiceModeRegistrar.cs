namespace GameBot.Service.Services.Installer;

internal sealed class ServiceModeRegistrar {
  public static RegistrationDecision Evaluate() {
    return new RegistrationDecision {
      RequiresElevation = true,
      StartupPolicy = "bootAutoStart"
    };
  }
}

internal sealed class RegistrationDecision {
  public bool RequiresElevation { get; set; }
  public string StartupPolicy { get; set; } = "manual";
}
