using System;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.Liveness;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// A liveness service with a report that the test sets (feature 106). Its options are not normalized,
/// so a test can use a short check interval.
/// </summary>
internal sealed class FakeSessionLivenessService : ISessionLivenessService {
  private DeviceLivenessReport _report = Live();
  private int _evaluations;

  public DeviceLivenessOptions Options { get; set; } = new() { QueueCheckIntervalMs = 20, QueueGracePeriodMs = 120000 };

  /// <summary>When set, <see cref="Evaluate"/> throws it.</summary>
  public Exception? Throws { get; set; }

  public DeviceLivenessReport Report {
    get => Volatile.Read(ref _report);
    set => Volatile.Write(ref _report, value);
  }

  public int Evaluations => Volatile.Read(ref _evaluations);

  public DeviceLivenessReport Evaluate(EmulatorSession session) {
    Interlocked.Increment(ref _evaluations);
    if (Throws is { } ex) throw ex;
    return Report;
  }

  public Task<SessionLivenessProbeResult> ProbeAsync(EmulatorSession session, CancellationToken ct) =>
    Task.FromResult(new SessionLivenessProbeResult(null, Evaluate(session)));

  public void SetNotLive(string reason) => Report = NotLive(reason);

  public void SetLive() => Report = Live();

  public static DeviceLivenessReport Live() => new(DeviceLivenessStates.Live, null, 100, 100, false, null, null, false);

  public static DeviceLivenessReport Unknown() => new(DeviceLivenessStates.Unknown, null, null, null, false, null, null, false);

  public static DeviceLivenessReport NotLive(string reason) =>
    new(DeviceLivenessStates.NotLive, reason, 90000, 90000, true, null, null, false);
}
