namespace GameBot.Service.Services.EnsureGameRunning;

internal interface IEnsureGameRunningActionHandler {
  Task<EnsureGameRunningActionResult> ExecuteAsync(string sessionId, CancellationToken ct = default);
}
