using GameBot.Domain.Updates;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// The check for an update (US1, US2). It reads the latest release, compares it with the installed
/// version, and keeps the last result. A failed check is a result, not an exception (FR-009).
/// </summary>
internal sealed class UpdateCheckService {
  private readonly IReleaseClient _client;
  private readonly IInstalledVersionProvider _installed;
  private readonly TimeProvider _time;
  private readonly ILogger<UpdateCheckService> _logger;

  private volatile UpdateCheckResult? _lastCheck;
  private volatile ReleaseInfo? _lastRelease;

  public UpdateCheckService(
      IReleaseClient client,
      IInstalledVersionProvider installed,
      TimeProvider time,
      ILogger<UpdateCheckService> logger) {
    _client = client;
    _installed = installed;
    _time = time;
    _logger = logger;
  }

  /// <summary>The last check in this bot run. Null before the first check.</summary>
  public UpdateCheckResult? LastCheck => _lastCheck;

  /// <summary>The release of the last check when that check found an update. Otherwise null.</summary>
  public ReleaseInfo? LastRelease => _lastRelease;

  public async Task<UpdateCheckResult> CheckAsync(CancellationToken ct) {
    var installed = _installed.GetInstalledVersion();
    UpdateCheckResult result;
    ReleaseInfo? release = null;

    try {
      var latest = await _client.GetLatestReleaseAsync(ct).ConfigureAwait(false);
      var status = UpdateVersionSelector.Evaluate(installed, latest.Version);
      release = status == UpdateCheckStatus.UpdateAvailable ? latest : null;
      result = new UpdateCheckResult(status, installed, latest.Version, latest.Notes, _time.GetUtcNow(), null);
      UpdateLog.CheckFinished(_logger, status.ToString(), installed.ToString(), latest.Version.ToString());
    }
    catch (UpdateFailureException ex) {
      result = new UpdateCheckResult(UpdateCheckStatus.CheckFailed, installed, null, null, _time.GetUtcNow(), ex.Error);
      UpdateLog.CheckFailed(_logger, ex.Error.Code, ex.Error.Message);
    }

    _lastRelease = release;
    _lastCheck = result;
    return result;
  }
}
