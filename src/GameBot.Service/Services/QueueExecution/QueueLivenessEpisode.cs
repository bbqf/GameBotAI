using System;
using System.Collections.Generic;
using GameBot.Domain.Sessions;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>A copy of the device-liveness state of one queue run (feature 106).</summary>
/// <param name="State">The last observed state.</param>
/// <param name="Reason">The last observed reason. Null when the state is not <c>not_live</c>.</param>
/// <param name="NotLiveSince">When the queue first observed the device as not live in this episode (local clock).</param>
/// <param name="GatedFirings">The number of held firings in the current episode.</param>
/// <param name="LastReport">The last observed report. Null before the first observation.</param>
/// <param name="AlertSent">The "device not live" alert is claimed for the open episode (feature 121).</param>
/// <param name="Attempts">The count of finished recovery attempts in the open episode (feature 121).</param>
/// <param name="RecoveryRunning">One recovery attempt runs for this queue (feature 121).</param>
internal sealed record QueueLivenessSnapshot(
  string State,
  string? Reason,
  DateTimeOffset? NotLiveSince,
  int GatedFirings,
  DeviceLivenessReport? LastReport,
  bool AlertSent = false,
  int Attempts = 0,
  bool RecoveryRunning = false);

/// <summary>
/// The fault episode of one queue run (feature 106, data-model section 8, research R-012). An episode
/// starts when the queue first observes <c>not_live</c> (each reason). It ends when the queue observes
/// <c>live</c> or <c>unknown</c>.
/// <para>
/// The episode keeps two separate records. The gate changes only <see cref="QueueLivenessSnapshot.GatedFirings"/>
/// and the set of sequences with a gate log entry (<see cref="RecordGatedFiring"/>). The watch changes only
/// the "fault cycle recorded" flag (<see cref="TryClaimFaultCycle"/>). Thus the watch records its fault cycle
/// also when the gate wrote entries.
/// </para>
/// One lock protects all fields.
/// </summary>
internal sealed class QueueLivenessEpisode {
  private readonly object _lock = new();
  private readonly HashSet<string> _gateLoggedSequences = new(StringComparer.Ordinal);
  private string _state = DeviceLivenessStates.Unknown;
  private string? _reason;
  private DateTimeOffset? _notLiveSince;
  private int _gatedFirings;
  private bool _faultCycleRecorded;
  private bool _alertSent;
  private bool _liveAgainPending;
  private bool _recoveryFailedSent;
  private int _attempts;
  private DateTimeOffset? _lastAttemptEndedAt;
  private bool _recoveryRunning;
  private DeviceLivenessReport? _lastReport;

  /// <summary>
  /// Stores <paramref name="report"/>. A <c>not_live</c> report with no open episode opens one at
  /// <paramref name="now"/>. A <c>live</c> report closes the episode and clears all episode data. It
  /// keeps the "live again" claim only when the alert was sent. An <c>unknown</c> report closes the
  /// episode and clears all data, with no "live again" claim.
  /// </summary>
  public void Observe(DeviceLivenessReport report, DateTimeOffset now) {
    ArgumentNullException.ThrowIfNull(report);
    lock (_lock) {
      _lastReport = report;
      _state = report.State;
      if (report.State == DeviceLivenessStates.NotLive) {
        _reason = report.Reason;
        _notLiveSince ??= now;
        return;
      }
      var isLive = report.State == DeviceLivenessStates.Live;
      // A live report after an alert sets the claim. A second live report keeps an unsent claim.
      // An unknown report removes the claim: no message for an episode that did not end live.
      _liveAgainPending = isLive && (_alertSent || _liveAgainPending);
      _reason = null;
      _notLiveSince = null;
      _gatedFirings = 0;
      _gateLoggedSequences.Clear();
      _faultCycleRecorded = false;
      _alertSent = false;
      _recoveryFailedSent = false;
      _attempts = 0;
      _lastAttemptEndedAt = null;
      _recoveryRunning = false;
    }
  }

  /// <summary>
  /// Returns true one time for each episode: an episode is open, it is older than
  /// <paramref name="after"/>, and the alert is not claimed yet.
  /// </summary>
  public bool TryClaimAlert(DateTimeOffset now, TimeSpan after) {
    lock (_lock) {
      if (_notLiveSince is not { } since || _alertSent) return false;
      if (now - since <= after) return false;
      _alertSent = true;
      return true;
    }
  }

  /// <summary>
  /// Returns true one time for each episode that closed with <c>live</c> after an alert.
  /// </summary>
  public bool TryClaimLiveAgain() {
    lock (_lock) {
      if (!_liveAgainPending) return false;
      _liveAgainPending = false;
      return true;
    }
  }

  /// <summary>
  /// Returns true one time for each episode: an episode is open and the "recovery failed" alert is not
  /// claimed yet.
  /// </summary>
  public bool TryClaimRecoveryFailed() {
    lock (_lock) {
      if (_notLiveSince is null || _recoveryFailedSent) return false;
      _recoveryFailedSent = true;
      return true;
    }
  }

  /// <summary>
  /// Claims the start of one recovery attempt. Returns true when an episode is open, it is older than
  /// <paramref name="after"/>, no recovery runs, <paramref name="maxAttempts"/> is not reached, and
  /// <paramref name="cooldown"/> passed since the last attempt. On true, <c>recoveryRunning</c> is set.
  /// </summary>
  public bool TryBeginRecovery(DateTimeOffset now, TimeSpan after, int maxAttempts, TimeSpan cooldown) {
    lock (_lock) {
      if (_notLiveSince is not { } since || _recoveryRunning) return false;
      if (now - since <= after || _attempts >= maxAttempts) return false;
      if (_lastAttemptEndedAt is { } ended && now - ended < cooldown) return false;
      _recoveryRunning = true;
      return true;
    }
  }

  /// <summary>
  /// Ends one recovery attempt. Counts the attempt, stores its end time, and clears
  /// <c>recoveryRunning</c>. Does nothing when no episode is open.
  /// </summary>
  public void EndRecoveryAttempt(DateTimeOffset now) {
    lock (_lock) {
      _recoveryRunning = false;
      if (_notLiveSince is null) return;
      _attempts++;
      _lastAttemptEndedAt = now;
    }
  }

  /// <summary>Clears <c>recoveryRunning</c> with no count. The runner uses it when a stop cancels an attempt.</summary>
  public void AbortRecoveryAttempt() {
    lock (_lock) {
      _recoveryRunning = false;
    }
  }

  /// <summary>
  /// Adds one held firing. Returns true only for the first held firing of <paramref name="sequenceId"/>
  /// in the episode: the gate then writes its one log entry.
  /// </summary>
  public bool RecordGatedFiring(string sequenceId) {
    lock (_lock) {
      _gatedFirings++;
      return _gateLoggedSequences.Add(sequenceId);
    }
  }

  /// <summary>
  /// Returns true one time for each episode: an episode is open, it is older than
  /// <paramref name="grace"/>, and its fault cycle is not recorded yet. It does not look at the gate
  /// log entries.
  /// </summary>
  public bool TryClaimFaultCycle(DateTimeOffset now, TimeSpan grace) {
    lock (_lock) {
      if (_notLiveSince is not { } since || _faultCycleRecorded) return false;
      if (now - since <= grace) return false;
      _faultCycleRecorded = true;
      return true;
    }
  }

  /// <summary>A copy for the queue health projection.</summary>
  public QueueLivenessSnapshot Snapshot() {
    lock (_lock) {
      return new QueueLivenessSnapshot(_state, _reason, _notLiveSince, _gatedFirings, _lastReport,
        _alertSent, _attempts, _recoveryRunning);
    }
  }
}
