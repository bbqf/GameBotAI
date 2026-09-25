using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Session;
using GameBot.Service.Services.Liveness;

namespace GameBot.ContractTests.Sessions;

/// <summary>
/// Test doubles for the feature 106 contract tests: a session manager whose sessions can have a device
/// serial with no real device, a transport check and a direct capture with fixed answers, a tracker
/// with a fixed sample, and a liveness service with a fixed report.
/// </summary>
internal sealed class LivenessFakeSessionManager : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  /// <summary>The dispatch result that <see cref="SendInputsWithResultsAsync"/> returns.</summary>
  public Func<IReadOnlyList<InputAction>, SessionInputDispatchResult>? Dispatch { get; set; }

  /// <summary>How many times <see cref="SendInputsWithResultsAsync"/> was called.</summary>
  public int DispatchCalls { get; private set; }

  /// <summary>When true, <see cref="GetSnapshotAsync"/> hangs until its token is cancelled.</summary>
  public bool HangSnapshots { get; set; }

  public int ActiveCount => _sessions.Count;
  public bool CanCreateSession => true;

  public EmulatorSession Add(string? serial) {
    var session = new EmulatorSession {
      Id = Guid.NewGuid().ToString("N"),
      GameId = "game-liveness",
      DeviceSerial = serial,
      Status = SessionStatus.Running
    };
    lock (_sessions) _sessions.Add(session);
    return session;
  }

  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) => Add(preferredDeviceSerial);
  public EmulatorSession? GetSession(string id) { lock (_sessions) return _sessions.FirstOrDefault(s => s.Id == id); }
  public IReadOnlyCollection<EmulatorSession> ListSessions() { lock (_sessions) return _sessions.ToList(); }
  public bool StopSession(string id) { lock (_sessions) return _sessions.RemoveAll(s => s.Id == id) > 0; }
  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) => Task.FromResult(actions.Count());

  public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) {
    DispatchCalls++;
    var list = actions.ToList();
    if (GetSession(id) is null) return Task.FromResult(new SessionInputDispatchResult(false, Array.Empty<InputActionResult>()));
    var result = Dispatch?.Invoke(list)
      ?? new SessionInputDispatchResult(true, list.Select((_, i) => new InputActionResult(i, true, null)).ToList());
    return Task.FromResult(result);
  }

  public async Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) {
    if (GetSession(id) is null) throw new KeyNotFoundException("Session not found");
    if (HangSnapshots) await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    return TinyPng;
  }

  /// <summary>A valid 1x1 PNG.</summary>
  public static readonly byte[] TinyPng = Convert.FromBase64String(
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR4nGNgYAAAAAMAASsJTYQAAAAASUVORK5CYII=");
}

internal sealed class FixedTransportCheck : ISessionTransportCheck {
  public SessionTransportCheckResult Result { get; set; } = new(true, "device", string.Empty, null);
  public Task<SessionTransportCheckResult> CheckAsync(string deviceSerial, CancellationToken ct) => Task.FromResult(Result);
}

internal sealed class FixedDirectCapture : ISessionDirectCapture {
  public bool Succeeds { get; set; } = true;
  public Task<bool> TryCaptureAsync(string deviceSerial, CancellationToken ct) => Task.FromResult(Succeeds);
}

/// <summary>A tracker that returns one fixed sample for each session, at a fixed time.</summary>
internal sealed class FixedSampleTracker : IDeviceLivenessTracker {
  public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
  public Func<bool, bool?, DeviceLivenessSample>? SampleFactory { get; set; }
  public bool CaptureData { get; set; }

  public void LoopStarted(string sessionId) { }
  public void LoopStopped(string sessionId) { }
  public void RecordCapture(string sessionId, bool changed) { }
  public void RecordInputStarted(string sessionId) { }
  public void RecordInputCompleted(string sessionId, InputOutcome outcome) { }
  public void Remove(string sessionId) { }

  public DeviceLivenessSample Sample(string sessionId, bool hasDevice, bool? transportReady = null) =>
    SampleFactory?.Invoke(hasDevice, transportReady)
    ?? new DeviceLivenessSample(hasDevice, transportReady, false, null, null, null, null, null, null);

  public bool HasCaptureData(string sessionId) => CaptureData;
}

/// <summary>A liveness service that returns one fixed report for each session.</summary>
internal sealed class FixedLivenessService : ISessionLivenessService {
  public DeviceLivenessOptions Options { get; set; } = new();
  public DeviceLivenessReport Report { get; set; } = Live();
  public int EvaluateCalls { get; private set; }

  public DeviceLivenessReport Evaluate(EmulatorSession session) {
    EvaluateCalls++;
    return Report;
  }

  public Task<SessionLivenessProbeResult> ProbeAsync(EmulatorSession session, CancellationToken ct) =>
    Task.FromResult(new SessionLivenessProbeResult(new SessionTransportCheckResult(true, "device", string.Empty, null), Report));

  public static DeviceLivenessReport Live() => new(DeviceLivenessStates.Live, null, 100, 100, false, null, null, false);

  public static DeviceLivenessReport NotLive(string reason) =>
    new(DeviceLivenessStates.NotLive, reason, 95012, 95012, true, null, "completed", false);
}
