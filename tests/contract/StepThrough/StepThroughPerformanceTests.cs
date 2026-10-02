#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;
using static GameBot.ContractTests.StepThrough.StepThroughFixture;

namespace GameBot.ContractTests.StepThrough;

/// <summary>
/// Performance goals of the step-through (feature 127, Constitution IV): the p95 of a state read is under
/// 50 ms (plan.md), the status shows within 1 second after a step ends (SC-003), and a step-through starts
/// and runs its first step in under 30 seconds (SC-001). The tests call the real service host through
/// the in-memory test server. The numbers go to the test output, for the performance note of the PR.
/// </summary>
public sealed class StepThroughPerformanceTests : IClassFixture<StepThroughFixture> {
  private readonly StepThroughFixture _f;
  private readonly ITestOutputHelper _output;

  public StepThroughPerformanceTests(StepThroughFixture fixture, ITestOutputHelper output) {
    _f = fixture;
    _output = output;
  }

  private static double Percentile(List<double> values, double percentile) {
    var sorted = values.OrderBy(v => v).ToList();
    var index = (int)Math.Ceiling(percentile * sorted.Count) - 1;
    return sorted[Math.Clamp(index, 0, sorted.Count - 1)];
  }

  [Fact]
  public async Task TheP95OfAStateReadWithAFullHistoryIsUnder50Ms() {
    // 60 steps, and one history entry for each step.
    var steps = Enumerable.Range(0, 60).Select(i => Tap(i, "s" + i)).ToArray();
    var sequenceId = await _f.SeedSequenceAsync("perf", steps);
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    for (var i = 0; i < 60; i++) {
      await _f.PostAsync($"/api/step-through/{id}/run-next");
      await _f.WaitIdleAsync(id);
    }

    await _f.GetAsync($"/api/step-through/{id}"); // warm up
    var times = new List<double>();
    for (var i = 0; i < 300; i++) {
      var sw = Stopwatch.StartNew();
      var (status, _) = await _f.GetAsync($"/api/step-through/{id}?afterSeq=0");
      sw.Stop();
      status.Should().Be(200);
      times.Add(sw.Elapsed.TotalMilliseconds);
    }

    var p95 = Percentile(times, 0.95);
    _output.WriteLine($"State read (60 history entries, 60 steps): p50 {Percentile(times, 0.5):F2} ms, p95 {p95:F2} ms, max {times.Max():F2} ms.");
    p95.Should().BeLessThan(50);
  }

  [Fact]
  public async Task TheStatusShowsWithinOneSecondAfterAStepEnds() {
    var sequenceId = await _f.SeedSequenceAsync("hold", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    _f.Sessions.Hold = new TaskCompletionSource<bool>();
    try {
      await _f.PostAsync($"/api/step-through/{id}/run-next");
      await Task.Delay(100);
      var sw = Stopwatch.StartNew();
      _f.Sessions.Hold.SetResult(true);

      var state = await _f.WaitIdleAsync(id, 5000);
      sw.Stop();

      state.GetProperty("history").GetArrayLength().Should().Be(1);
      _output.WriteLine($"Delay from the end of the step to the visible status (polled each 25 ms): {sw.Elapsed.TotalMilliseconds:F1} ms.");
      sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
    }
    finally {
      _f.Sessions.Hold = null;
    }
  }

  [Fact]
  public async Task AStepThroughStartsAndRunsItsFirstStepInUnder30Seconds() {
    var sequenceId = await _f.SeedSequenceAsync("first", Tap(0, "a"), Tap(1, "b"));
    var sw = Stopwatch.StartNew();

    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    var state = await _f.WaitIdleAsync(id);
    sw.Stop();

    state.GetProperty("history")[0].GetProperty("status").GetString().Should().Be("Succeeded");
    _output.WriteLine($"Start to the end of the first step (no emulator): {sw.Elapsed.TotalMilliseconds:F1} ms.");
    sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
  }
}
