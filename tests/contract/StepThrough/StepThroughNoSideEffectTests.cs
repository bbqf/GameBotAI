#pragma warning disable CA2007, CA1861, CA1859, CA1849, CA1054, CA1056, CA1307, CA1308
using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using Xunit;
using static GameBot.ContractTests.StepThrough.StepThroughFixture;

namespace GameBot.ContractTests.StepThrough;

/// <summary>
/// SC-006: a step-through changes no queue schedule and no daily record. A step with an outside effect shows
/// its intended effect in the history and does not apply it (feature 127, FR-015).
/// </summary>
public sealed class StepThroughNoSideEffectTests : IClassFixture<StepThroughFixture> {
  private readonly StepThroughFixture _f;

  public StepThroughNoSideEffectTests(StepThroughFixture fixture) { _f = fixture; }

  private static SequenceStep Reschedule(int order, string id, string option, string? offset = null) {
    var step = new SequenceStep {
      Order = order,
      StepId = id,
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf }
    };
    step.Action.Parameters["option"] = option;
    if (offset is not null) step.Action.Parameters["timerRelativeOffset"] = offset;
    return step;
  }

  [Theory]
  [InlineData("Timer", "00:10:00", "would reschedule at")]
  [InlineData("OncePerRun", null, "would run this sequence again in this run")]
  [InlineData("EveryStep", null, "after every step")]
  [InlineData("AtQueueStart", null, "next start of the queue")]
  [InlineData("Cancel", null, "would remove the pending bookings")]
  public async Task RescheduleSelfShowsItsEffectAndChangesNoQueueSchedule(string option, string? offset, string expectedEffect) {
    var serial = "emu-" + Guid.NewGuid().ToString("N")[..8];
    using var queue = _f.AttachQueue(serial);
    var sequenceId = await _f.SeedSequenceAsync("resched", Reschedule(0, "r", option, offset));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession(serial));
    (await _f.PostAsync($"/api/step-through/{id}/pause-queue")).Status.Should().Be(200);

    (await _f.PostAsync($"/api/step-through/{id}/run-next")).Status.Should().Be(202);
    var state = await _f.WaitIdleAsync(id);

    var entry = state.GetProperty("history")[0];
    entry.GetProperty("status").GetString().Should().Be("Succeeded");
    entry.GetProperty("outcome").GetString().Should().Be("previewed");
    entry.GetProperty("effects")[0].GetString().Should().Contain(expectedEffect);

    queue.Handle.SnapshotPendingTimerFirings().Should().BeEmpty();
    queue.Handle.PendingOncePerRun.Should().BeEmpty();
    queue.Handle.PendingNextCycleStart.Should().BeEmpty();
    queue.Handle.EveryStepInjections.Should().BeEmpty();
    queue.Handle.PendingLiveSchedules.Should().BeEmpty();
    queue.Handle.HasPendingSelfRescheduleWork.Should().BeFalse();
  }

  [Fact]
  public async Task RescheduleSelfWithNoQueueAtAllStillOnlyPreviews() {
    var sequenceId = await _f.SeedSequenceAsync("resched", Reschedule(0, "r", "Timer", "00:05:00"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    await _f.PostAsync($"/api/step-through/{id}/run-next");
    var state = await _f.WaitIdleAsync(id);

    state.GetProperty("history")[0].GetProperty("effects")[0].GetString().Should().StartWith("would reschedule at");
  }

  [Fact]
  public async Task NotifyIsPreviewedAndSendsNothing() {
    var notify = new SequenceStep {
      Order = 0,
      StepId = "n",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = ActionTypes.Notify }
    };
    notify.Action.Parameters["message"] = "The gems ran out";
    var sequenceId = await _f.SeedSequenceAsync("notify", notify);
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());

    await _f.PostAsync($"/api/step-through/{id}/run-next");
    var state = await _f.WaitIdleAsync(id);

    var entry = state.GetProperty("history")[0];
    entry.GetProperty("outcome").GetString().Should().Be("previewed");
    entry.GetProperty("effects")[0].GetString().Should().Contain("would send a notification").And.Contain("The gems ran out");
  }

  [Fact]
  public async Task AStepRunIsWrittenToTheExecutionLogWithTheStepThroughOrigin() {
    var sequenceId = await _f.SeedSequenceAsync("logged", Tap(0, "a"));
    var id = await _f.StartAsync(sequenceId, _f.NewGameSession());
    await _f.PostAsync($"/api/step-through/{id}/run-next");
    var state = await _f.WaitIdleAsync(id);
    var logId = state.GetProperty("history")[0].GetProperty("executionLogId").GetString();

    var (status, entry) = await _f.GetAsync($"/api/execution-logs/{logId}");

    status.Should().Be(200);
    entry.GetProperty("origin").GetString().Should().Be("step-through");
    entry.GetProperty("finalStatus").GetString().Should().Be("success");

    var (_, list) = await _f.GetAsync("/api/execution-logs?origin=step-through&pageSize=200");
    list.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetString()).Should().Contain(logId);
    var (_, others) = await _f.GetAsync("/api/execution-logs?origin=other&pageSize=200");
    others.GetProperty("items").GetArrayLength().Should().Be(0);
  }
}
