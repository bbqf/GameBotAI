using GameBot.Domain.Updates;

namespace GameBot.Service.Services.Updates;

/// <summary>
/// Reads the result file of the last update at bot start (research R-008) and keeps the result in
/// memory for <c>GET /api/update/status</c>. The new bot can start before the updater writes the final
/// state, so the reporter waits a short time for a final state. If none comes, it decides from the
/// running version: the target version means success.
/// </summary>
internal sealed class UpdateResultReporter {
  private readonly IUpdateResultStore _store;
  private readonly IInstalledVersionProvider _installed;
  private readonly TimeProvider _time;
  private readonly ILogger<UpdateResultReporter> _logger;
  private readonly TimeSpan _finalStateWait;
  private readonly TimeSpan _pollInterval;

  private volatile UpdateAttempt? _lastResult;

  public UpdateResultReporter(
      IUpdateResultStore store,
      IInstalledVersionProvider installed,
      TimeProvider time,
      ILogger<UpdateResultReporter> logger)
    : this(store, installed, time, logger, TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(250)) { }

  public UpdateResultReporter(
      IUpdateResultStore store,
      IInstalledVersionProvider installed,
      TimeProvider time,
      ILogger<UpdateResultReporter> logger,
      TimeSpan finalStateWait,
      TimeSpan pollInterval) {
    _store = store;
    _installed = installed;
    _time = time;
    _logger = logger;
    _finalStateWait = finalStateWait;
    _pollInterval = pollInterval;
  }

  /// <summary>A finished attempt from before the last restart. Null when there is none.</summary>
  public UpdateAttempt? LastResult => _lastResult;

  public async Task LoadAsync(CancellationToken ct) {
    var result = await _store.ReadAsync(ct).ConfigureAwait(false);
    if (result is null) {
      return;
    }

    var waited = TimeSpan.Zero;
    while (!IsFinal(result) && waited < _finalStateWait) {
      await Task.Delay(_pollInterval, ct).ConfigureAwait(false);
      waited += _pollInterval;
      result = await _store.ReadAsync(ct).ConfigureAwait(false) ?? result;
    }

    if (!IsFinal(result)) {
      result = Decide(result);
    }

    _lastResult = result;
    _store.MarkReported();
    UpdateLog.ResultReported(_logger, result.AttemptId, result.State.ToString());

    if (result.State == UpdateState.Succeeded) {
      _store.Cleanup(_time.GetUtcNow());
    }
  }

  private static bool IsFinal(UpdateAttempt attempt) =>
    attempt.State is UpdateState.Succeeded or UpdateState.Failed;

  private UpdateAttempt Decide(UpdateAttempt attempt) {
    var running = _installed.GetInstalledVersion().ToString();
    if (string.Equals(running, attempt.TargetVersion, StringComparison.Ordinal)) {
      return attempt with { State = UpdateState.Succeeded, FinishedAtUtc = _time.GetUtcNow() };
    }

    return attempt with {
      State = UpdateState.Failed,
      FinishedAtUtc = _time.GetUtcNow(),
      ErrorCode = "update_install_failed",
      ErrorMessage = "The update did not finish.",
      ErrorHint = "Try the update again."
    };
  }
}
