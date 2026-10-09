using GameBot.Service.Services.Updates;

namespace GameBot.Service.Hosted;

/// <summary>
/// Reads the result of the last update before the first status request (feature 131). It does its
/// work in <c>StartAsync</c>, so the host does not accept requests until it finishes.
/// </summary>
internal sealed class UpdateResultReportingService : IHostedService {
  private readonly UpdateResultReporter _reporter;
  private readonly ILogger<UpdateResultReportingService> _logger;

  public UpdateResultReportingService(UpdateResultReporter reporter, ILogger<UpdateResultReportingService> logger) {
    _reporter = reporter;
    _logger = logger;
  }

  public async Task StartAsync(CancellationToken cancellationToken) {
    try {
      await _reporter.LoadAsync(cancellationToken).ConfigureAwait(false);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
      // A damaged update folder must not stop the bot from starting.
      UpdateLog.ResultUnreadable(_logger, ex.Message);
    }
  }

  public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
