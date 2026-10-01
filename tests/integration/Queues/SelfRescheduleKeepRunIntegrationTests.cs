using System;
using System.Collections.Generic;
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
/// Feature 125: end-to-end queue runs that book Timer firings with and without <c>keep: earliest</c>,
/// exercised through the real DI graph (queue engine, real sequence dispatch, coordinator, register).
/// ADB is stubbed (GAMEBOT_USE_ADB=false). A Timer booking keeps the non-cycling run alive, so a test
/// reads the pending booking from the run register and then stops the queue.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SelfRescheduleKeepRunIntegrationTests {
  public SelfRescheduleKeepRunIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static SequenceStep TimerStep(int order, string offset, bool keep) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    action.Parameters["option"] = "Timer";
    action.Parameters["timerRelativeOffset"] = offset;
    if (keep) action.Parameters["keep"] = "earliest";
    return new SequenceStep { Order = order, StepId = $"r{order}", StepType = SequenceStepType.Action, Action = action };
  }

  private static SequenceStep OcrStep(int order, string fallback, bool keep) {
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    action.Parameters["option"] = "Timer";
    action.Parameters["ocrOffset"] = new Dictionary<string, object?> {
      ["region"] = new Dictionary<string, object?> { ["x"] = 1, ["y"] = 2, ["width"] = 30, ["height"] = 10 },
      ["fallback"] = fallback
    };
    if (keep) action.Parameters["keep"] = "earliest";
    return new SequenceStep { Order = order, StepId = $"o{order}", StepType = SequenceStepType.Action, Action = action };
  }

  private static async Task SeedAsync(IServiceProvider services, CommandSequence sequence, string queueId, int entries = 1) {
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    for (var i = 0; i < entries; i++) {
      template.Entries.Add(new QueueTemplateEntry { SequenceId = sequence.Id, ScheduleType = ScheduleType.OncePerRun });
    }
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline",
      CycleExecution = false, LinkedTemplateId = template.Id
    }).ConfigureAwait(false);
  }

  /// <summary>Waits until the pending Timer booking of the sequence is close to <paramref name="minutes"/> ahead.</summary>
  private static async Task<SelfRescheduleEntry?> WaitForPendingAsync(IServiceProvider services, string queueId, string sequenceId, double minutes) {
    var registry = services.GetRequiredService<IQueueRunRegistry>();
    var sw = Stopwatch.StartNew();
    while (sw.ElapsedMilliseconds < 30000) {
      if (registry.TryGet(queueId, out var handle)) {
        var entry = handle.SnapshotPendingTimerFirings().FirstOrDefault(e => e.SequenceId == sequenceId);
        if (entry?.FireAt is { } fireAt) {
          var ahead = (fireAt - DateTimeOffset.Now).TotalMinutes;
          if (ahead > minutes - 1 && ahead <= minutes + 0.2) {
            // Give the run a moment to finish the remaining steps, then read again.
            await Task.Delay(500).ConfigureAwait(false);
            return handle.SnapshotPendingTimerFirings().FirstOrDefault(e => e.SequenceId == sequenceId);
          }
        }
      }
      await Task.Delay(50).ConfigureAwait(false);
    }
    return null;
  }

  private static async Task<SelfRescheduleEntry?> RunAndReadPendingAsync(
      CommandSequence sequence, string queueId, double minutes, int entries = 1) {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, sequence, queueId, entries).ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync(queueId).ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    try {
      return await WaitForPendingAsync(services, queueId, sequence.Id, minutes).ConfigureAwait(false);
    }
    finally {
      await engine.StopAsync(queueId).ConfigureAwait(false);
    }
  }

  [Fact] // SC-001
  public async Task FourKeepEarliestBookingsLeaveTheEarliestPending() {
    var sequence = new CommandSequence { Id = "seq-keep-4", Name = "Keep4" };
    // The earliest booking is the last step, so the expected value shows only after all four steps ran.
    sequence.SetSteps(new[] {
      TimerStep(0, "00:50:00", true), TimerStep(1, "00:30:00", true), TimerStep(2, "00:40:00", true), TimerStep(3, "00:15:00", true)
    });

    var pending = await RunAndReadPendingAsync(sequence, "q-keep-4", 15).ConfigureAwait(false);

    pending.Should().NotBeNull();
    ((pending!.FireAt!.Value - DateTimeOffset.Now).TotalMinutes).Should().BeInRange(14, 15.2);
    pending.RunId.Should().NotBeNullOrWhiteSpace();
  }

  [Fact] // The first booking is the earliest. Later bookings must not replace it.
  public async Task LaterKeepEarliestBookingsDoNotReplaceTheFirstOne() {
    var sequence = new CommandSequence { Id = "seq-keep-first", Name = "KeepFirst" };
    sequence.SetSteps(new[] {
      TimerStep(0, "00:15:00", true), TimerStep(1, "00:50:00", true), TimerStep(2, "00:30:00", true), TimerStep(3, "00:40:00", true)
    });

    var pending = await RunAndReadPendingAsync(sequence, "q-keep-first", 15).ConfigureAwait(false);

    pending.Should().NotBeNull();
    ((pending!.FireAt!.Value - DateTimeOffset.Now).TotalMinutes).Should().BeInRange(14, 15.2);
  }

  [Fact] // The compare uses the fire time after the OCR fallback (no session in this test, so the fallback applies).
  public async Task OcrOffsetBookingsCompareTheEffectiveFireTime() {
    var sequence = new CommandSequence { Id = "seq-keep-ocr", Name = "KeepOcr" };
    sequence.SetSteps(new[] {
      OcrStep(0, "00:30:00", true), OcrStep(1, "00:10:00", true), OcrStep(2, "00:20:00", true)
    });

    var pending = await RunAndReadPendingAsync(sequence, "q-keep-ocr", 10).ConfigureAwait(false);

    pending.Should().NotBeNull();
    ((pending!.FireAt!.Value - DateTimeOffset.Now).TotalMinutes).Should().BeInRange(9, 10.2);
  }

  [Fact] // A booking of another run replaces the pending booking, also an earlier one.
  public async Task ABookingOfAnotherRunReplacesTheEarlierPendingBooking() {
    // Two OncePerRun entries of the same sequence make two executions with two run ids.
    // Each execution books 10 minutes, then 30 minutes (kept back, same run).
    var sequence = new CommandSequence { Id = "seq-keep-runs", Name = "KeepRuns" };
    sequence.SetSteps(new[] { TimerStep(0, "00:10:00", true), TimerStep(1, "00:30:00", true) });

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, sequence, "q-keep-runs", entries: 2).ConfigureAwait(false);
    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync("q-keep-runs").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    try {
      var log = services.GetRequiredService<IExecutionLogService>();
      var sw = Stopwatch.StartNew();
      List<ExecutionLogEntry> runs;
      do {
        await Task.Delay(100).ConfigureAwait(false);
        var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", PageSize = 100 }).ConfigureAwait(false);
        runs = page.Items.Where(e => e.ObjectRef.ObjectId == sequence.Id && e.FinalStatus != "running").ToList();
      } while (runs.Count < 2 && sw.ElapsedMilliseconds < 30000);
      runs.Should().HaveCount(2);

      services.GetRequiredService<IQueueRunRegistry>().TryGet("q-keep-runs", out var handle).Should().BeTrue();
      var pending = handle!.SnapshotPendingTimerFirings().Should().ContainSingle().Subject;
      var latestRun = runs.OrderBy(e => e.TimestampUtc).Last();
      pending.RunId.Should().Be(latestRun.Id, "the first booking of the later run replaces the booking of the earlier run");
    }
    finally {
      await engine.StopAsync("q-keep-runs").ConfigureAwait(false);
    }
  }
}
