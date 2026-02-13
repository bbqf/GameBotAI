namespace GameBot.IntegrationTests.Helpers;

internal sealed class InstallerTestEnvironment {
  public string DataRoot { get; init; } = Path.Combine(Path.GetTempPath(), "GameBot", "InstallerTests");
  public string ConfigFile => Path.Combine(DataRoot, "config", "installer-config.json");
}
