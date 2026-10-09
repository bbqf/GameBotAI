namespace GameBot.Service.Hosted;

/// <summary>
/// Writes the problems found in the saved network settings to the log when the host starts.
/// The address is chosen before the logger exists, so the problems wait here (feature 132).
/// </summary>
internal sealed class NetworkSettingsProblemLogger : IHostedService {
  private readonly IReadOnlyList<string> _problems;
  private readonly ILogger<NetworkSettingsProblemLogger> _logger;

  public NetworkSettingsProblemLogger(IReadOnlyList<string> problems, ILogger<NetworkSettingsProblemLogger> logger) {
    _problems = problems;
    _logger = logger;
  }

  private static readonly Action<ILogger, string, Exception?> s_problem =
    LoggerMessage.Define<string>(LogLevel.Warning, new EventId(1, "NetworkSettingsProblem"), "{Problem}");

  public Task StartAsync(CancellationToken cancellationToken) {
    foreach (var problem in _problems) {
      s_problem(_logger, problem, null);
    }

    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
