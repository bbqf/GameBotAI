using System;
using System.Diagnostics;
using System.Linq;
using FluentAssertions;
using GameBot.Service.Services.QueueExecution;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859

namespace GameBot.Tests.Unit.Performance;

/// <summary>
/// Feature 079 performance budget (Constitution Principle IV): a device claim/release pair is a single
/// <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey,TValue}"/> operation each and
/// must stay under 0.05 ms, so adding it to the queue-start path costs nothing measurable next to the
/// 10-100 ms ADB round-trips the run performs immediately afterwards.
/// </summary>
public sealed class DeviceClaimBenchmarkTests {
  private const int Iterations = 20_000;
  private const double BudgetMsPerOperation = 0.05;

  [Fact]
  public void ClaimAndReleaseStayWithinTheDeclaredBudget() {
    var registry = new DeviceClaimRegistry();

    // Warm up so JIT and first-allocation costs do not land in the measurement.
    for (var i = 0; i < 1_000; i++) {
      registry.TryClaim("warmup-serial", "q", "Q");
      registry.Release("warmup-serial", "q");
    }

    var sw = Stopwatch.StartNew();
    for (var i = 0; i < Iterations; i++) {
      registry.TryClaim("emulator-5558", "q1", "Daily").Should().BeTrue();
      registry.Release("emulator-5558", "q1");
    }
    sw.Stop();

    var msPerPair = sw.Elapsed.TotalMilliseconds / Iterations;
    msPerPair.Should().BeLessThan(BudgetMsPerOperation * 2,
      "a claim+release pair must stay within twice the per-operation budget");
  }

  [Fact]
  public void HolderLookupIsConstantTimeAcrossManyClaimedDevices() {
    var registry = new DeviceClaimRegistry();
    foreach (var i in Enumerable.Range(0, 500)) {
      registry.TryClaim($"emulator-{i}", $"q{i}", $"Queue {i}");
    }

    var sw = Stopwatch.StartNew();
    for (var i = 0; i < Iterations; i++) {
      registry.TryGetHolder("emulator-499", out _).Should().BeTrue();
    }
    sw.Stop();

    (sw.Elapsed.TotalMilliseconds / Iterations).Should().BeLessThan(BudgetMsPerOperation);
  }
}
