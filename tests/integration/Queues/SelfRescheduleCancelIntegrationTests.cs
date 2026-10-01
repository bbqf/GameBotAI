using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Domain.Queues;
using GameBot.Domain.QueueTemplates;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Queues;

/// <summary>
/// Feature 123: queue runs that use the reschedule-self option Cancel, through the real queue engine.
/// ADB is stubbed. The queue does not cycle, so each run is deterministic.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SelfRescheduleCancelIntegrationTests {
  public SelfRescheduleCancelIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static SequenceStep Reschedule(int order, string stepId, string option, string? relativeOffset = null) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    action.Parameters["option"] = option;
    if (relativeOffset is not null) action.Parameters["timerRelativeOffset"] = relativeOffset;
    return new SequenceStep { Order = order, StepId = stepId, StepType = SequenceStepType.Action, Action = action };
  }

  // A step that always succeeds and needs no device (wait-for-image with no target and no timeout).
  private static SequenceStep Marker(int order) => new() {
    Order = order, StepId = $"marker{order}", StepType = SequenceStepType.Action,
    WaitForImage = new WaitForImageConfig { TimeoutMs = 0 }
  };

  // A step that fails by name: it refers to a command that does not exist.
  private static SequenceStep FailingStep(int order) => new() {
    Order = order, StepId = $"fail{order}", StepType = SequenceStepType.Command, CommandId = "no-such-command"
  };

  private static async Task SeedAsync(IServiceProvider services, string sequenceId, string queueId, params SequenceStep[] steps) {
    var sequence = new CommandSequence { Id = sequenceId, Name = sequenceId };
    sequence.SetSteps(steps);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = sequenceId, ScheduleType = ScheduleType.OncePerRun });
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);

    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline",
      CycleExecution = false, LinkedTemplateId = template.Id
    }).ConfigureAwait(false);
  }

  private static async Task<ExecutionLogEntry?> RunToCompletionAsync(IServiceProvider services, string queueId) {
    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync(queueId).ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 20000) {
      await Task.Delay(20).ConfigureAwait(false);
    }
    engine.IsRunning(queueId).Should().BeFalse("a Cancel step leaves no booking, so the run ends at once");
    var page = await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "queue", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false);
    return page.Items.FirstOrDefault(e => e.ObjectRef.ObjectId == queueId);
  }

  private static async Task<int> CountSequenceFiringsAsync(IServiceProvider services, string sequenceId) {
    var page = await services.GetRequiredService<IExecutionLogService>()
      .QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", PageSize = 200 }).ConfigureAwait(false);
    return page.Items.Count(e => e.ObjectRef.ObjectId == sequenceId);
  }

  private static async Task WaitForFiringsAsync(IServiceProvider services, string sequenceId, int expected, int timeoutMs) {
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < timeoutMs && await CountSequenceFiringsAsync(services, sequenceId).ConfigureAwait(false) < expected) {
      await Task.Delay(100).ConfigureAwait(false);
    }
  }

  [Fact] // SC-001: Timer at step 0 and a final Cancel leave no booking and no retry wake.
  public async Task TimerThenCancelLeavesNoRetry() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-timer-cancel", "q-timer-cancel",
      Reschedule(0, "book", "Timer", "00:00:02"), Marker(1), Reschedule(2, "cancel", "Cancel")).ConfigureAwait(false);

    var entry = await RunToCompletionAsync(services, "q-timer-cancel").ConfigureAwait(false);

    entry.Should().NotBeNull();
    entry!.Summary.Should().Contain("1 sequence(s) executed");
    (await CountSequenceFiringsAsync(services, "seq-timer-cancel").ConfigureAwait(false)).Should().Be(1);
  }

  [Fact] // SC-002, FR-006: a run that fails by name before Cancel keeps its booking, so the retry wake occurs.
  public async Task FailureBeforeCancelKeepsTheBooking() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-fail-cancel", "q-fail-cancel",
      Reschedule(0, "book", "Timer", "00:00:01"), FailingStep(1), Reschedule(2, "cancel", "Cancel")).ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync("q-fail-cancel").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    try {
      await WaitForFiringsAsync(services, "seq-fail-cancel", 2, 60000).ConfigureAwait(false);
    }
    finally {
      await engine.StopAsync("q-fail-cancel").ConfigureAwait(false);
    }

    (await CountSequenceFiringsAsync(services, "seq-fail-cancel").ConfigureAwait(false)).Should().BeGreaterThanOrEqualTo(2);
  }

  [Fact] // A booking made after Cancel stays: the last booking wins.
  public async Task BookingAfterCancelStillFires() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-rebook", "q-cancel-rebook",
      Reschedule(0, "book1", "Timer", "00:00:05"), Reschedule(1, "cancel", "Cancel"), Reschedule(2, "book2", "Timer", "00:00:01")).ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync("q-cancel-rebook").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    try {
      await WaitForFiringsAsync(services, "seq-cancel-rebook", 2, 60000).ConfigureAwait(false);
    }
    finally {
      await engine.StopAsync("q-cancel-rebook").ConfigureAwait(false);
    }

    (await CountSequenceFiringsAsync(services, "seq-cancel-rebook").ConfigureAwait(false)).Should().BeGreaterThanOrEqualTo(2);
  }

  [Fact] // FR-015: a Cancel in the first booked firing stops the second booking in the same drain copy.
  public async Task CancelStopsTheSecondBookingInTheDrainCopy() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    // The entry run books two OncePerRun firings (b1, b2). b1 runs the same sequence: its Cancel
    // marks b2 in the drain copy, so b2 must not fire. b1 books two more (b3, b4) for a later drain
    // that a non-cycling run does not make.
    await SeedAsync(services, "seq-drain", "q-drain",
      Reschedule(0, "cancel", "Cancel"), Reschedule(1, "book1", "OncePerRun"), Reschedule(2, "book2", "OncePerRun")).ConfigureAwait(false);

    var entry = await RunToCompletionAsync(services, "q-drain").ConfigureAwait(false);

    entry.Should().NotBeNull();
    // Entry run + b1 = 2 executed. Without the drain skip, b2 would fire too (3 executed).
    entry!.Summary.Should().Contain("2 sequence(s) executed");
  }

  [Fact] // SC-003: a sequence with only a Cancel step succeeds in a queue with no booking.
  public async Task CancelOnlySequenceSucceedsInAQueue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, "seq-cancel-only", "q-cancel-only", Reschedule(0, "cancel", "Cancel")).ConfigureAwait(false);

    var entry = await RunToCompletionAsync(services, "q-cancel-only").ConfigureAwait(false);

    entry.Should().NotBeNull();
    entry!.FinalStatus.Should().Be("success");
    entry.Summary.Should().Contain("1 sequence(s) executed");
  }
}
