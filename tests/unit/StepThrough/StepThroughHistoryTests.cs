#pragma warning disable CA2007, CA1861, CA1859, CA1849
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Parameters;
using GameBot.Domain.Services.StepThrough;
using GameBot.Service.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepThroughServiceRig;

namespace GameBot.UnitTests.StepThrough;

/// <summary>History, restart, and execution log of a step-through (feature 127, FR-004a, FR-008, FR-009, FR-016).</summary>
public sealed class StepThroughHistoryTests {
  private static SequenceStep Loop(int order, string id, int count, params SequenceStep[] body)
    => new() { Order = order, StepId = id, StepType = SequenceStepType.Loop, Loop = new CountLoopConfig { Count = count }, Body = body };

  [Fact]
  public async Task EachStepRunIsOneEntryAndLoopEntriesCarryTheIterationNumber() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("loop", Loop(0, "loop", 2, Cmd(0, "in1"), Cmd(1, "in2")), Cmd(1, "after"));
    var id = await rig.StartAsync(sequenceId);

    StepThroughStateDto state = null!;
    while ((state = rig.Service.Get(id).Value!).State != "complete") state = await rig.RunAsync(id);

    var steps = state.History.Where(h => h.Kind == "step").ToList();
    steps.Select(h => h.Path).Should().Equal("0/body/0", "0/body/1", "0/body/0", "0/body/1", "1");
    steps.Select(h => h.Iteration).Should().Equal(1, 1, 2, 2, null);
    state.History.Select(h => h.Seq).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
    rig.Wiring.Executed.Should().Equal("in1", "in2", "in1", "in2", "after");
  }

  [Fact]
  public async Task AfterSeqReturnsOnlyNewerEntries() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("three", Cmd(0, "a"), Cmd(1, "b"), Cmd(2, "c"));
    var id = await rig.StartAsync(sequenceId);
    await rig.RunAsync(id);
    await rig.RunAsync(id);
    await rig.RunAsync(id);

    var newer = rig.Service.Get(id, afterSeq: 1).Value!;

    newer.History.Select(h => h.Seq).Should().Equal(2, 3);
    rig.Service.Get(id, afterSeq: 3).Value!.History.Should().BeEmpty();
  }

  [Fact]
  public async Task HistoryKeepsOnlyTheNewestThousandEntries() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("many", Loop(0, "loop", 1100, Cmd(0, "in")));
    var id = await rig.StartAsync(sequenceId);

    StepThroughStateDto state = rig.Service.Get(id).Value!;
    for (var i = 0; i < 1100 && state.State != "complete"; i++) state = await rig.RunAsync(id);

    state.History.Count.Should().Be(StepperState.HistoryCap);
    state.History.Select(h => h.Seq).Should().BeInAscendingOrder();
    state.History[0].Seq.Should().BeGreaterThan(1);
  }

  [Fact]
  public async Task AnActionWithAnOutsideEffectShowsItsEffectInTheHistory() {
    var rig = new StepThroughServiceRig();
    var resched = Cmd(0, "r");
    resched.StepType = SequenceStepType.Action;
    resched.Action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    resched.CommandId = string.Empty;
    var sequenceId = await rig.SeedAsync("resched", resched);
    var id = await rig.StartAsync(sequenceId);

    var state = await rig.RunAsync(id);

    state.History.Single().Effects.Should().Equal("would run reschedule-self");
    rig.Wiring.Executed.Should().BeEmpty();
  }

  [Fact]
  public async Task RestartClearsHistoryAndOutcomesAndKeepsValuesAndTheQueuePause() {
    var rig = new StepThroughServiceRig();
    var queue = rig.AttachQueue();
    var sequence = new CommandSequence { Id = "seq-param", Name = "param" };
    sequence.SetSteps(new[] { Cmd(0, "a"), Cmd(1, "b") });
    sequence.Parameters.Add(new ParameterDeclaration { Name = "n" });
    await rig.Sequences.CreateAsync(sequence);
    var id = await rig.StartAsync("seq-param", values: new Dictionary<string, string> { ["n"] = "3" });
    rig.Service.PauseQueue(id).IsSuccess.Should().BeTrue();
    await rig.RunAsync(id);
    rig.Service.Get(id).Value!.Outcomes.Should().ContainKey("a");

    var restarted = (await rig.Service.RestartAsync(id)).Value!;

    restarted.History.Should().BeEmpty();
    restarted.Outcomes.Should().NotContainKey("a");
    restarted.Cursor.Should().Be("0");
    restarted.Parameters.Single().Value.Should().Be("3");
    restarted.Queue!.PausedByStepThrough.Should().BeTrue();
    queue.Handle.IsPolicyPaused.Should().BeTrue();
  }

  [Fact]
  public async Task AStepRunWritesOneExecutionLogEntryWithTheStepThroughOrigin() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("logged", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);

    var state = await rig.RunAsync(id);

    rig.Log.Started.Should().ContainSingle().Which.Origin.Should().Be(ExecutionOrigins.StepThrough);
    var finalized = rig.Log.Finalized.Should().ContainSingle().Subject;
    finalized.Status.Should().Be("success");
    finalized.Context.Origin.Should().Be(ExecutionOrigins.StepThrough);
    finalized.Details.Should().ContainSingle().Which.Attributes!["origin"].Should().Be(ExecutionOrigins.StepThrough);
    state.History.Single().ExecutionLogId.Should().Be("log-1");
    rig.Wiring.LastRequest!.RootExecutionId.Should().Be("log-1");
  }

  [Fact]
  public async Task AFailedStepIsLoggedAsAFailure() {
    var rig = new StepThroughServiceRig();
    rig.Wiring.Failure = _ => new InvalidOperationException("boom");
    var sequenceId = await rig.SeedAsync("fails", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);

    var state = await rig.RunAsync(id);

    state.History.Single().Status.Should().Be("Failed");
    rig.Log.Finalized.Single().Status.Should().Be("failure");
  }

  [Fact]
  public async Task ADownExecutionLogDoesNotStopTheStep() {
    var rig = new StepThroughServiceRig();
    rig.Log.FailWrites = true;
    var sequenceId = await rig.SeedAsync("nolog", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);

    var state = await rig.RunAsync(id);

    state.History.Single().Status.Should().Be("Succeeded", state.History.Single().Message);
    state.History.Single().ExecutionLogId.Should().BeNull();
    rig.Wiring.Executed.Should().Equal("a");
  }

  [Fact]
  public async Task ACancelledStepAddsACancelledEntryAndKeepsTheCursor() {
    var rig = new StepThroughServiceRig();
    rig.Wiring.Hold = new TaskCompletionSource<bool>();
    var sequenceId = await rig.SeedAsync("slow", Cmd(0, "a"), Cmd(1, "b"));
    var id = await rig.StartAsync(sequenceId);
    (await rig.Service.RunNextAsync(id)).IsSuccess.Should().BeTrue();
    rig.Service.Get(id).Value!.State.Should().Be("running");
    rig.Service.Get(id).Value!.Running!.Path.Should().Be("0");

    var cancel = rig.Service.Cancel(id);
    await rig.Service.RunningTask(id)!;

    cancel.Value!.WasRunning.Should().BeTrue();
    var state = rig.Service.Get(id).Value!;
    state.State.Should().Be("idle");
    state.Cursor.Should().Be("0");
    state.History.Single().Status.Should().Be("Cancelled");
    rig.Log.Finalized.Single().Status.Should().Be("failure");
  }

  [Fact]
  public async Task NoSequenceTimeLimitScopeIsActiveDuringAStep() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("one", Cmd(0, "a"));
    var id = await rig.StartAsync(sequenceId);

    await rig.RunAsync(id);

    rig.Wiring.TimeLimitScopeSeen.Should().BeFalse();
  }

  [Fact]
  public async Task ComputedVersionChangesWhenTheStoredSequenceChanges() {
    var rig = new StepThroughServiceRig();
    var sequenceId = await rig.SeedAsync("v", Cmd(0, "a"));
    var first = StepThroughService.ComputeVersion((await rig.Sequences.GetAsync(sequenceId))!);
    var again = StepThroughService.ComputeVersion((await rig.Sequences.GetAsync(sequenceId))!);
    var changed = (await rig.Sequences.GetAsync(sequenceId))!;
    changed.SetSteps(new[] { Cmd(0, "a"), Cmd(1, "b") });

    again.Should().Be(first);
    StepThroughService.ComputeVersion(changed).Should().NotBe(first);
  }
}
