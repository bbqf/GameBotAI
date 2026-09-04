using GameBot.Domain.Config;
using GameBot.Domain.Queues;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.Service.Hosted;

/// <summary>
/// Restarts a queue run whose emulator has stopped answering ADB.
/// <para>
/// An emulator can disappear mid-run — the instance crashes, the host reclaims it, ADB loses the
/// transport — and nothing downstream notices. The session goes on reporting <c>Running</c>, the
/// foreground guard cannot help because there is no device to foreground onto, and every firing
/// after that dispatches input into nothing. Observed in the field as a queue that stayed "Running"
/// for hours while its every sequence failed on "'swipe' input was not accepted".
/// </para>
/// <para>
/// The instance is only ever brought up on the way into a run
/// (<c>EnsureEmulatorBeforeSessionAsync</c>), so the cheapest correct recovery is to end the run and
/// start it again: that path already relaunches the instance, waits for boot, and binds a fresh
/// session. The cost is the run's live self-reschedules, which is why recovery is deliberately slow
/// to trigger and rate-limited.
/// </para>
/// </summary>
internal sealed partial class QueueDeviceWatchdogService : BackgroundService {
  // Resolved per sweep rather than injected: a hosted service is constructed while the host is
  // starting, and taking the queue-execution graph as a constructor dependency would force all of it
  // into existence at startup, ahead of everything that expects to build lazily on first use.
  private readonly IServiceProvider _serviceProvider;
  private readonly AppConfig _config;
  private readonly TimeProvider _timeProvider;
  private readonly ILogger<QueueDeviceWatchdogService> _logger;

  // Consecutive failed probes per queue. Reset the moment a probe succeeds, so only a sustained
  // outage accumulates strikes.
  private readonly Dictionary<string, int> _strikes = new(StringComparer.Ordinal);
  // When each queue was last restarted by this watchdog, for the cooldown.
  private readonly Dictionary<string, DateTimeOffset> _lastHeal = new(StringComparer.Ordinal);

  public QueueDeviceWatchdogService(
      IServiceProvider serviceProvider,
      AppConfig config,
      ILogger<QueueDeviceWatchdogService> logger,
      TimeProvider? timeProvider = null) {
    _serviceProvider = serviceProvider;
    _config = config;
    _logger = logger;
    _timeProvider = timeProvider ?? TimeProvider.System;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken) {
    var intervalMs = _config.QueueDeviceWatchdogIntervalMs;
    if (intervalMs <= 0) {
      Log.Disabled(_logger);
      return;
    }

    // The whole watchdog is an ADB probe: with ADB switched off there is nothing it can learn, and
    // shelling out to adb anyway would be pure interference (this is the switch the test hosts use).
    if (string.Equals(Environment.GetEnvironmentVariable("GAMEBOT_USE_ADB"), "false", StringComparison.OrdinalIgnoreCase)) {
      Log.Disabled(_logger);
      return;
    }

    var interval = TimeSpan.FromMilliseconds(Math.Max(1000, intervalMs));
    while (!stoppingToken.IsCancellationRequested) {
      try {
        await Task.Delay(interval, _timeProvider, stoppingToken).ConfigureAwait(false);
        using var scope = _serviceProvider.CreateScope();
        await SweepAsync(
          scope.ServiceProvider.GetRequiredService<IEmulatorDeviceProbe>(),
          scope.ServiceProvider.GetRequiredService<IQueueExecutionService>(),
          scope.ServiceProvider.GetRequiredService<IQueueRepository>(),
          stoppingToken).ConfigureAwait(false);
      }
      catch (OperationCanceledException) {
        break;
      }
      catch (Exception ex) {
        Log.SweepFailed(_logger, ex);
      }
    }
  }

  internal async Task SweepAsync(
      IEmulatorDeviceProbe probe,
      IQueueExecutionService execution,
      IQueueRepository queues,
      CancellationToken ct) {
    // Nothing to police on a host that cannot probe ADB at all (non-Windows, ADB disabled in tests).
    if (!probe.IsAvailable) return;

    var strikeLimit = Math.Max(1, _config.QueueDeviceWatchdogStrikes);
    var cooldown = TimeSpan.FromMilliseconds(Math.Max(0, _config.QueueDeviceWatchdogCooldownMs));

    foreach (var queue in await queues.ListAsync().ConfigureAwait(false)) {
      ct.ThrowIfCancellationRequested();
      if (string.IsNullOrWhiteSpace(queue.EmulatorSerial)) continue;
      if (!execution.IsRunning(queue.Id)) {
        _strikes.Remove(queue.Id);
        continue;
      }

      if (await probe.IsResponsiveAsync(queue.EmulatorSerial, ct).ConfigureAwait(false)) {
        _strikes.Remove(queue.Id);
        continue;
      }

      var strikes = _strikes.TryGetValue(queue.Id, out var n) ? n + 1 : 1;
      _strikes[queue.Id] = strikes;
      Log.ProbeFailed(_logger, queue.Id, queue.EmulatorSerial, strikes, strikeLimit);
      if (strikes < strikeLimit) continue;

      var now = _timeProvider.GetUtcNow();
      if (_lastHeal.TryGetValue(queue.Id, out var last) && now - last < cooldown) {
        Log.HealSuppressed(_logger, queue.Id, queue.EmulatorSerial);
        continue;
      }

      _strikes.Remove(queue.Id);
      _lastHeal[queue.Id] = now;
      Log.Healing(_logger, queue.Id, queue.EmulatorSerial);
      try {
        // Stop first: the run is holding a device claim and a dead session, and the start path
        // refuses a queue whose device is still claimed.
        await execution.StopAsync(queue.Id, ct).ConfigureAwait(false);
        var outcome = await execution.StartAsync(queue.Id, ct).ConfigureAwait(false);
        // Passed as the enum, not ToString(): the generated logger only formats it once it knows the
        // level is enabled (CA1873).
        Log.Healed(_logger, queue.Id, queue.EmulatorSerial, outcome);
      }
      catch (OperationCanceledException) { throw; }
      catch (Exception ex) {
        Log.HealFailed(_logger, queue.Id, queue.EmulatorSerial, ex);
      }
    }
  }

  private static partial class Log {
    [LoggerMessage(EventId = 7200, Level = LogLevel.Information, Message = "Queue device watchdog disabled by configuration.")]
    public static partial void Disabled(ILogger logger);

    [LoggerMessage(EventId = 7201, Level = LogLevel.Warning, Message = "Queue {QueueId} device {Serial} did not answer ADB ({Strikes}/{Limit}).")]
    public static partial void ProbeFailed(ILogger logger, string queueId, string serial, int strikes, int limit);

    [LoggerMessage(EventId = 7202, Level = LogLevel.Warning, Message = "Queue {QueueId} device {Serial} is unresponsive; restarting the run to bring the emulator back.")]
    public static partial void Healing(ILogger logger, string queueId, string serial);

    [LoggerMessage(EventId = 7203, Level = LogLevel.Information, Message = "Queue {QueueId} restarted after device {Serial} was unresponsive: {Outcome}.")]
    public static partial void Healed(ILogger logger, string queueId, string serial, QueueStartOutcome outcome);

    [LoggerMessage(EventId = 7204, Level = LogLevel.Error, Message = "Queue {QueueId} restart after device {Serial} went unresponsive failed.")]
    public static partial void HealFailed(ILogger logger, string queueId, string serial, Exception ex);

    [LoggerMessage(EventId = 7205, Level = LogLevel.Warning, Message = "Queue {QueueId} device {Serial} still unresponsive but a restart is within the cooldown; leaving it alone.")]
    public static partial void HealSuppressed(ILogger logger, string queueId, string serial);

    [LoggerMessage(EventId = 7206, Level = LogLevel.Warning, Message = "Queue device watchdog sweep failed.")]
    public static partial void SweepFailed(ILogger logger, Exception ex);
  }
}
