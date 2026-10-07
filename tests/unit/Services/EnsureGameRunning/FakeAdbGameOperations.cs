using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameBot.Service.Services.EnsureGameRunning;

// Test-code analyzer relaxations permitted by the constitution:
#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.UnitTests.Services.EnsureGameRunning;

/// <summary>
/// A fake of <see cref="IAdbGameOperations"/>. It records each call and its serial, in call order.
/// A call can fail, throw, or never return.
/// </summary>
internal sealed class FakeAdbGameOperations : IAdbGameOperations {
  private readonly object _lock = new();

  public string? ForegroundPackage { get; set; }

  /// <summary>When set, the foreground check calls this and ignores <see cref="ForegroundPackage"/>.</summary>
  public Func<string?>? ForegroundScript { get; set; }

  public List<string> LaunchedPackages { get; } = new();
  public List<string> StoppedSerials { get; } = new();
  public List<string> LaunchSerials { get; } = new();
  public List<string> ProbeSerials { get; } = new();

  /// <summary>The order of calls: "stop", "launch", "try-launch", "probe".</summary>
  public List<string> CallOrder { get; } = new();

  public bool StopResult { get; set; } = true;
  public bool StartResult { get; set; } = true;
  public Exception? StopThrows { get; set; }
  public Exception? StartThrows { get; set; }
  public Exception? ProbeThrows { get; set; }

  /// <summary>The call waits for the cancel and then ends with a cancel.</summary>
  public bool StopHangs { get; set; }
  public bool StartHangs { get; set; }
  public bool ProbeHangs { get; set; }

  /// <summary>The hanging call does not look at the cancel token. It never ends.</summary>
  public bool HangsIgnoreCancel { get; set; }

  public int TotalCalls {
    get { lock (_lock) return CallOrder.Count; }
  }

  private void Record(string kind, List<string>? serials, string serial) {
    lock (_lock) {
      CallOrder.Add(kind);
      serials?.Add(serial);
    }
  }

  private Task Hang(CancellationToken ct) =>
    HangsIgnoreCancel
      ? new TaskCompletionSource().Task
      : Task.Delay(Timeout.Infinite, ct);

  public async Task<string?> GetForegroundPackageAsync(string deviceSerial, CancellationToken ct = default) {
    Record("probe", ProbeSerials, deviceSerial);
    if (ProbeHangs) await Hang(ct);
    if (ProbeThrows is not null) throw ProbeThrows;
    return ForegroundScript is not null ? ForegroundScript() : ForegroundPackage;
  }

  public Task LaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) {
    Record("launch", LaunchSerials, deviceSerial);
    lock (_lock) LaunchedPackages.Add(packageName);
    return Task.CompletedTask;
  }

  public async Task<bool> ForceStopAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) {
    Record("stop", StoppedSerials, deviceSerial);
    if (StopHangs) await Hang(ct);
    if (StopThrows is not null) throw StopThrows;
    return StopResult;
  }

  public async Task<bool> TryLaunchAppAsync(string deviceSerial, string packageName, CancellationToken ct = default) {
    Record("try-launch", LaunchSerials, deviceSerial);
    lock (_lock) LaunchedPackages.Add(packageName);
    if (StartHangs) await Hang(ct);
    if (StartThrows is not null) throw StartThrows;
    return StartResult;
  }
}
