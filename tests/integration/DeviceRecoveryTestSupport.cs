using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Domain.Sessions;
using GameBot.Emulator.Adb;
using GameBot.Emulator.Session;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.Liveness;
using GameBot.Service.Services.Notifications;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

#pragma warning disable CA2007, CA2000, CA1812 // test code

namespace GameBot.IntegrationTests;

/// <summary>
/// The simulated devices of the feature 121 flow tests. A device is blocked until its instance is
/// rebooted. The serial of device N is <c>emu-N</c> and the instance name is <c>LDPlayer-N</c>.
/// </summary>
internal sealed class SimulatedDevices {
  private readonly ConcurrentDictionary<string, bool> _blocked = new(StringComparer.OrdinalIgnoreCase);
  private readonly ConcurrentDictionary<string, string> _instanceOfSerial = new(StringComparer.OrdinalIgnoreCase);

  public static string SerialOf(int n) => $"emu-{n}";

  public static string InstanceOf(int n) => $"LDPlayer-{n}";

  public void Block(int n) => Block(SerialOf(n), InstanceOf(n));

  /// <summary>Blocks the device <paramref name="serial"/>. A reboot of <paramref name="instanceName"/> unblocks it.</summary>
  public void Block(string serial, string instanceName) {
    _instanceOfSerial[serial] = instanceName;
    _blocked[serial] = true;
  }

  public bool IsBlocked(string? serial) => serial is not null && _blocked.TryGetValue(serial, out var b) && b;

  public void UnblockInstance(string? instanceName) {
    if (instanceName is null) return;
    foreach (var pair in _instanceOfSerial) {
      if (string.Equals(pair.Value, instanceName, StringComparison.OrdinalIgnoreCase)) _blocked[pair.Key] = false;
    }
  }
}

/// <summary>
/// A session manager whose sessions keep the device serial. The stub session manager of the no-adb mode
/// gives no serial, and the simulated liveness needs it.
/// </summary>
internal sealed class SerialSessionManager : ISessionManager {
  private readonly List<EmulatorSession> _sessions = new();

  public int ActiveCount { get { lock (_sessions) return _sessions.Count; } }

  public bool CanCreateSession => true;

  public EmulatorSession CreateSession(string gameIdOrPath, string? preferredDeviceSerial = null) {
    var session = new EmulatorSession {
      Id = Guid.NewGuid().ToString("N"),
      GameId = gameIdOrPath,
      DeviceSerial = preferredDeviceSerial,
      Status = SessionStatus.Running
    };
    lock (_sessions) _sessions.Add(session);
    return session;
  }

  public EmulatorSession? GetSession(string id) { lock (_sessions) return _sessions.FirstOrDefault(s => s.Id == id); }

  public IReadOnlyCollection<EmulatorSession> ListSessions() { lock (_sessions) return _sessions.ToList(); }

  public bool StopSession(string id) { lock (_sessions) return _sessions.RemoveAll(s => s.Id == id) > 0; }

  public Task<int> SendInputsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
    Task.FromResult(actions.Count());

  public Task<SessionInputDispatchResult> SendInputsWithResultsAsync(string id, IEnumerable<InputAction> actions, CancellationToken ct = default) =>
    Task.FromResult(new SessionInputDispatchResult(true, Array.Empty<InputActionResult>()));

  public Task<byte[]> GetSnapshotAsync(string id, CancellationToken ct = default) => Task.FromResult(Array.Empty<byte>());
}

/// <summary>A liveness service: not live while the device of the session is blocked.</summary>
internal sealed class SimulatedLivenessService : ISessionLivenessService {
  private readonly SimulatedDevices _devices;

  public SimulatedLivenessService(SimulatedDevices devices, DeviceLivenessOptions options) {
    _devices = devices;
    Options = options;
  }

  public DeviceLivenessOptions Options { get; }

  public DeviceLivenessReport Evaluate(EmulatorSession session) =>
    _devices.IsBlocked(session.DeviceSerial)
      ? new DeviceLivenessReport(DeviceLivenessStates.NotLive, DeviceLivenessReasons.CaptureStalled, 90000, 90000, true, null, null, false)
      : new DeviceLivenessReport(DeviceLivenessStates.Live, null, 100, 100, false, null, null, false);

  public Task<SessionLivenessProbeResult> ProbeAsync(EmulatorSession session, CancellationToken ct) =>
    Task.FromResult(new SessionLivenessProbeResult(null, Evaluate(session)));
}

/// <summary>An instance control that records each reboot start and unblocks the device at the end.</summary>
internal sealed class SimulatedEmulatorControl : IEmulatorControl {
  private readonly SimulatedDevices _devices;
  private int _reboots;

  public SimulatedEmulatorControl(SimulatedDevices devices) {
    _devices = devices;
  }

  public ConcurrentQueue<(string? Name, DateTimeOffset StartedAt)> Starts { get; } = new();

  public int Reboots => Volatile.Read(ref _reboots);

  /// <summary>How long a reboot command takes.</summary>
  public TimeSpan Duration { get; set; } = TimeSpan.FromMilliseconds(100);

  /// <summary>When true, a reboot waits until its token is cancelled.</summary>
  public bool Hang { get; set; }

  public bool IsAvailable => true;

  public Task<LdConsoleRunState> GetRunStateAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) =>
    Task.FromResult(LdConsoleRunState.Running);

  public Task LaunchAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) => Task.CompletedTask;

  public async Task<bool> RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    Interlocked.Increment(ref _reboots);
    Starts.Enqueue((instanceName, DateTimeOffset.UtcNow));
    if (Hang) await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
    await Task.Delay(Duration, ct).ConfigureAwait(false);
    _devices.UnblockInstance(instanceName);
    return true;
  }
}

/// <summary>A probe that always says the device is ready.</summary>
internal sealed class ReadyProbe : IEmulatorDeviceProbe {
  public bool IsAvailable => true;

  public Task<bool> IsResponsiveAsync(string adbSerial, CancellationToken ct = default) => Task.FromResult(true);
}

/// <summary>
/// A dispatcher that records each alert and completes it at once, as a worker with one good target does.
/// </summary>
internal sealed class AlertCapturingDispatcher : INotificationDispatcher {
  private readonly ConcurrentQueue<QueueAlert> _alerts = new();

  public IReadOnlyList<QueueAlert> Alerts => _alerts.ToList();

  public int Count(QueueAlertKind kind, string? queueId = null) =>
    _alerts.Count(a => a.Kind == kind && (queueId is null || a.QueueId == queueId));

  public void Enqueue(QueueNotificationJob job) { }

  public void ResetStreaks(string queueId) { }

  public void SendAlert(QueueAlert alert) {
    _alerts.Enqueue(alert);
    alert.OnCompleted?.Invoke(DateTimeOffset.Now, true, null);
  }
}

/// <summary>Builds hosts and queues for the feature 121 flow tests.</summary>
internal static class DeviceRecoveryHost {
  public static WebApplicationFactory<Program> Create(
    SimulatedDevices devices,
    SimulatedEmulatorControl control,
    AlertCapturingDispatcher dispatcher,
    int alertAfterMs = 1000,
    int staggerMs = 0) {
    var options = new DeviceLivenessOptions {
      QueueCheckIntervalMs = 50,
      QueueGracePeriodMs = 600000,
      AlertAfterMs = alertAfterMs,
      RecoveryStaggerMs = staggerMs,
      RebootReadyTimeoutMs = 10000
    };
    return new WebApplicationFactory<Program>().WithWebHostBuilder(b => b.ConfigureTestServices(s => {
      s.RemoveAll<ISessionManager>();
      s.AddSingleton<ISessionManager>(new SerialSessionManager());
      s.RemoveAll<ISessionLivenessService>();
      s.AddSingleton<ISessionLivenessService>(new SimulatedLivenessService(devices, options));
      s.RemoveAll<IEmulatorControl>();
      s.AddSingleton<IEmulatorControl>(control);
      s.RemoveAll<IEmulatorDeviceProbe>();
      s.AddSingleton<IEmulatorDeviceProbe>(new ReadyProbe());
      s.RemoveAll<INotificationDispatcher>();
      s.AddSingleton<INotificationDispatcher>(dispatcher);
      s.PostConfigure<DeviceLivenessOptions>(o => {
        o.QueueCheckIntervalMs = options.QueueCheckIntervalMs;
        o.QueueGracePeriodMs = options.QueueGracePeriodMs;
        o.AlertAfterMs = options.AlertAfterMs;
        o.RecoveryStaggerMs = options.RecoveryStaggerMs;
        o.RebootReadyTimeoutMs = options.RebootReadyTimeoutMs;
      });
    }));
  }

  /// <summary>Writes a template with one once-per-run entry and a queue for device <paramref name="n"/>. Returns the queue ID.</summary>
  public static async Task<string> CreateQueueAsync(WebApplicationFactory<Program> app, int n, QueueDeviceRecovery? recovery, string? sequenceId = null) {
    var templates = app.Services.GetRequiredService<IQueueTemplateRepository>();
    var template = new QueueTemplate { Id = "tpl-" + Guid.NewGuid().ToString("N"), Name = "Recovery " + Guid.NewGuid().ToString("N") };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = sequenceId ?? "seq-" + Guid.NewGuid().ToString("N"), ScheduleType = ScheduleType.OncePerRun });
    template = await templates.CreateAsync(template);
    var queues = app.Services.GetRequiredService<IQueueRepository>();
    var queue = await queues.CreateAsync(new ExecutionQueue {
      Id = "q-" + Guid.NewGuid().ToString("N"),
      Name = "Recovery " + n,
      EmulatorSerial = SimulatedDevices.SerialOf(n),
      EmulatorInstanceName = SimulatedDevices.InstanceOf(n),
      CycleExecution = false,
      LinkedTemplateId = template.Id,
      DeviceRecovery = recovery
    });
    return queue.Id;
  }

  public static async Task WaitForAsync(Func<bool> condition, int timeoutMs = 15000) {
    var end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
    while (!condition() && DateTime.UtcNow < end) await Task.Delay(25);
  }

  public static JsonElement Parse(string body) => JsonDocument.Parse(body).RootElement.Clone();
}
