using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Emulator.Adb;
using GameBot.Service.Services.EnsureEmulatorRunning;
using GameBot.Service.Services.QueueExecution;

namespace GameBot.UnitTests.Queues.Fakes;

/// <summary>
/// A coordinator that the test controls (feature 121). It records each call. By default a reboot ends
/// with the result in <see cref="Result"/> after the optional <see cref="Gate"/> is open.
/// </summary>
internal sealed class FakeDeviceRecoveryCoordinator : IDeviceRecoveryCoordinator {
  private int _calls;

  public ConcurrentQueue<string> Instances { get; } = new();

  public int Calls => Volatile.Read(ref _calls);

  /// <summary>The result of each call. Default true.</summary>
  public bool Result { get; set; } = true;

  /// <summary>When set, each call throws it.</summary>
  public Exception? Throws { get; set; }

  /// <summary>When set, each call waits until this task ends.</summary>
  public Task? Gate { get; set; }

  public async Task<bool> RebootInstanceAsync(string instanceName, CancellationToken ct) {
    Interlocked.Increment(ref _calls);
    Instances.Enqueue(instanceName);
    if (Throws is { } ex) throw ex;
    if (Gate is { } gate) await gate.WaitAsync(ct).ConfigureAwait(false);
    return Result;
  }
}

/// <summary>
/// An <see cref="IEmulatorControl"/> that records the local start time of each reboot (feature 121).
/// </summary>
internal sealed class FakeEmulatorControl : IEmulatorControl {
  private int _reboots;

  public ConcurrentQueue<(string? Name, DateTimeOffset StartedAt)> Starts { get; } = new();

  public int Reboots => Volatile.Read(ref _reboots);

  /// <summary>The result of each reboot. Default true.</summary>
  public bool Result { get; set; } = true;

  /// <summary>When set, each reboot throws it.</summary>
  public Exception? Throws { get; set; }

  /// <summary>How long each reboot takes (real time).</summary>
  public TimeSpan Duration { get; set; } = TimeSpan.Zero;

  /// <summary>When set, each reboot waits until this task ends.</summary>
  public Task? Gate { get; set; }

  public bool IsAvailable => true;

  public Task<LdConsoleRunState> GetRunStateAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) =>
    Task.FromResult(LdConsoleRunState.Running);

  public Task LaunchAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) => Task.CompletedTask;

  public async Task<bool> RebootAsync(string? instanceName, int? instanceIndex, CancellationToken ct = default) {
    Interlocked.Increment(ref _reboots);
    Starts.Enqueue((instanceName, DateTimeOffset.UtcNow));
    if (Throws is { } ex) throw ex;
    if (Gate is { } gate) await gate.WaitAsync(ct).ConfigureAwait(false);
    if (Duration > TimeSpan.Zero) await Task.Delay(Duration, ct).ConfigureAwait(false);
    return Result;
  }
}
