using System;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Queues;
using Xunit;

#pragma warning disable CA2007, CA1861, CA1859, CA2000

namespace GameBot.UnitTests.Queues;

// Feature 173 (FR-019): the settings of a running queue can be saved. A run reads the queue once, at its start, and the
// repository gives each caller its own copy. So a saved setting applies at the next start. These tests keep that true.
public sealed partial class QueueExecutionServiceTests {
  /// <summary>Saves new settings the way the update route does: a changed copy replaces the stored queue.</summary>
  private static async Task SaveSettingsAsync(Harness h, string id, Action<ExecutionQueue> change) {
    var stored = await h.Queues.GetAsync(id);
    var copy = new ExecutionQueue {
      Id = stored!.Id,
      Name = stored.Name,
      EmulatorSerial = stored.EmulatorSerial,
      CycleExecution = stored.CycleExecution,
      LinkedTemplateId = stored.LinkedTemplateId,
      PauseWhenIdle = stored.PauseWhenIdle,
      IdleThresholdSeconds = stored.IdleThresholdSeconds
    };
    change(copy);
    await h.Queues.UpdateAsync(copy);
  }

  [Fact]
  public async Task ASavedCycleSettingDoesNotChangeTheRunningRun() {
    var h = new Harness();
    AddQueueWithEntries(h, "q1", new[] { OncePerRun("A") }, cycle: true);
    h.Sequences.Handler = async (id, ct) => { await Task.Delay(5, ct); return FakeSequenceExecution.Success(id); };

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Count >= 2);
    await SaveSettingsAsync(h, "q1", q => q.CycleExecution = false);
    var countAtSave = h.Sequences.Executed.Count;
    await WaitForAsync(() => h.Sequences.Executed.Count >= countAtSave + 3);

    h.Sequences.Executed.Count.Should().BeGreaterThanOrEqualTo(countAtSave + 3, "the run keeps cycling");
    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    handle.CycleExecution.Should().BeTrue();
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task PauseWhenIdleSwitchedOnDuringARunDoesNotStartAnIdleHold() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S"), RelativeTimer("T", TimeSpan.FromMinutes(10)) },
      cycle: true, pauseWhenIdle: false);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("S"), 10000);
    await SaveSettingsAsync(h, "q1", q => { q.PauseWhenIdle = true; q.IdleThresholdSeconds = 1; });
    await Task.Delay(500);

    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    handle.IsIdlePaused.Should().BeFalse();
    h.Sessions.HomeCount.Should().Be(0);
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task PauseWhenIdleSwitchedOffDuringARunDoesNotEndTheIdleHold() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S"), RelativeTimer("T", TimeSpan.FromMinutes(10)) },
      cycle: true, pauseWhenIdle: true, idleThresholdSeconds: 30);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Registry.TryGet("q1", out var handle) && handle.IsIdlePaused, 10000);
    await SaveSettingsAsync(h, "q1", q => q.PauseWhenIdle = false);
    await Task.Delay(500);

    h.Registry.TryGet("q1", out var held).Should().BeTrue();
    held.IsIdlePaused.Should().BeTrue();
    await h.Service.StopAsync("q1");
  }

  [Fact]
  public async Task AnIdleThresholdSavedDuringARunDoesNotChangeTheRunningRun() {
    var clock = new FakeTimeProvider(FakeStart);
    var h = new Harness(clock);
    // The gap to the timer is 10 minutes. The threshold of 3600 seconds is longer, so the run does not hold.
    AddQueueWithEntries(h, "q1", new[] { AtQueueStart("S"), RelativeTimer("T", TimeSpan.FromMinutes(10)) },
      cycle: true, pauseWhenIdle: true, idleThresholdSeconds: 3600);

    await h.Service.StartAsync("q1");
    await WaitForAsync(() => h.Sequences.Executed.Contains("S"), 10000);
    await SaveSettingsAsync(h, "q1", q => q.IdleThresholdSeconds = 30);
    await Task.Delay(500);

    h.Registry.TryGet("q1", out var handle).Should().BeTrue();
    handle.IsIdlePaused.Should().BeFalse();
    h.Sessions.HomeCount.Should().Be(0);
    await h.Service.StopAsync("q1");
  }
}
