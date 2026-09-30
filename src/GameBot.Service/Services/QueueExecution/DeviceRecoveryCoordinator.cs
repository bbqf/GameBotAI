using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Domain.Sessions;
using GameBot.Service.Services.EnsureEmulatorRunning;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace GameBot.Service.Services.QueueExecution;

/// <summary>
/// Implementation of <see cref="IDeviceRecoveryCoordinator"/> (feature 121, research R-006). One
/// <see cref="SemaphoreSlim"/> is the slot. The slot covers the wait for the stagger time and the reboot
/// command. It does not cover the wait for the boot: the runner of each queue does that wait outside the
/// slot. This class calls only <see cref="IEmulatorControl.RebootAsync"/>.
/// </summary>
internal sealed class DeviceRecoveryCoordinator : IDeviceRecoveryCoordinator, IDisposable {
  /// <summary>One reboot that callers for one instance share.</summary>
  private sealed class InFlight {
    public InFlight(CancellationTokenSource cts) => Cts = cts;

    public CancellationTokenSource Cts { get; }
    public Task<bool> Task { get; set; } = System.Threading.Tasks.Task.FromResult(false);
    public int Waiters { get; set; }
    public bool Started { get; set; }
  }

  private readonly SemaphoreSlim _slot = new(1, 1);
  private readonly object _gate = new();
  private readonly Dictionary<string, InFlight> _inFlight = new(StringComparer.OrdinalIgnoreCase);
  private readonly IEmulatorControl _control;
  private readonly IOptions<DeviceLivenessOptions>? _options;
  private readonly TimeProvider _time;
  private readonly ILogger _logger;
  private DateTimeOffset? _lastStartedAt;

  public DeviceRecoveryCoordinator(
    IEmulatorControl control,
    IOptions<DeviceLivenessOptions>? options = null,
    TimeProvider? time = null,
    ILogger<DeviceRecoveryCoordinator>? logger = null) {
    _control = control;
    _options = options;
    _time = time ?? TimeProvider.System;
    _logger = logger ?? NullLogger<DeviceRecoveryCoordinator>.Instance;
  }

  /// <inheritdoc />
  public async Task<bool> RebootInstanceAsync(string instanceName, CancellationToken ct) {
    ArgumentException.ThrowIfNullOrWhiteSpace(instanceName);
    ct.ThrowIfCancellationRequested();

    InFlight flight;
    lock (_gate) {
      if (!_inFlight.TryGetValue(instanceName, out flight!) || flight.Cts.IsCancellationRequested) {
        flight = new InFlight(new CancellationTokenSource());
        _inFlight[instanceName] = flight;
        var created = flight;
        created.Task = Task.Run(() => RunAsync(instanceName, created));
      }
      flight.Waiters++;
    }

    try {
      return await flight.Task.WaitAsync(ct).ConfigureAwait(false);
    }
    catch (OperationCanceledException) when (ct.IsCancellationRequested) {
      // The last waiter that leaves cancels the flight: the wait for the slot, the stagger wait, or the
      // wait for the reboot command. The slot is then free. A reboot command that already started is not
      // killed: the tool ends by itself, and only the wait for its end stops.
      lock (_gate) {
        flight.Waiters--;
        if (flight.Waiters == 0) {
          try {
#pragma warning disable CA1849 // inside a lock: no await possible. The cancel only wakes the wait for the slot.
            flight.Cts.Cancel();
#pragma warning restore CA1849
          }
          catch (ObjectDisposedException) {
            // The flight already ended.
          }
        }
      }
      throw;
    }
  }

  private async Task<bool> RunAsync(string instanceName, InFlight flight) {
    var token = flight.Cts.Token;
    try {
      await _slot.WaitAsync(token).ConfigureAwait(false);
      try {
        await WaitForStaggerAsync(token).ConfigureAwait(false);
        lock (_gate) {
          flight.Started = true;
        }
        _lastStartedAt = _time.GetUtcNow();
        CoordinatorLog.RebootStarting(_logger, instanceName);
        return await _control.RebootAsync(instanceName, null, token).ConfigureAwait(false);
      }
      finally {
        _slot.Release();
      }
    }
    catch (OperationCanceledException) {
      return false;
    }
    catch (Exception ex) {
      CoordinatorLog.RebootFaulted(_logger, instanceName, ex);
      return false;
    }
    finally {
      lock (_gate) {
        if (_inFlight.TryGetValue(instanceName, out var current) && ReferenceEquals(current, flight)) {
          _inFlight.Remove(instanceName);
        }
      }
      flight.Cts.Dispose();
    }
  }

  /// <summary>Releases the slot semaphore.</summary>
  public void Dispose() => _slot.Dispose();

  private async Task WaitForStaggerAsync(CancellationToken token) {
    if (_lastStartedAt is not { } last) return;
    var stagger = TimeSpan.FromMilliseconds((_options?.Value ?? new DeviceLivenessOptions()).Normalized().RecoveryStaggerMs);
    var remaining = last + stagger - _time.GetUtcNow();
    if (remaining > TimeSpan.Zero) {
      await Task.Delay(remaining, _time, token).ConfigureAwait(false);
    }
  }
}

internal static partial class CoordinatorLog {
  [LoggerMessage(EventId = 1157, Level = LogLevel.Information, Message = "Device recovery: the service starts a reboot of instance {Instance}.")]
  public static partial void RebootStarting(ILogger logger, string Instance);

  [LoggerMessage(EventId = 1158, Level = LogLevel.Warning, Message = "Device recovery: the reboot of instance {Instance} failed.")]
  public static partial void RebootFaulted(ILogger logger, string Instance, Exception ex);
}
