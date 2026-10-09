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
/// Feature 065: end-to-end queue runs that self-reschedule, exercised through the real DI graph
/// (queue engine → real SequenceExecutionService dispatch → coordinator → run-loop draining).
/// ADB is stubbed (GAMEBOT_USE_ADB=false); a non-cycling queue gives a deterministic single pass.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SelfRescheduleRunIntegrationTests {
  public SelfRescheduleRunIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  // Builds sequence S = [ marker step (always succeeds), reschedule-self OncePerRun
  // gated on the marker's outcome ]. expectedState selects whether the IF branch fires.
  // The marker is a wait-for-image step with no detection target and a zero timeout — an
  // infrastructure-free step that records a "success" outcome. (It used to be a dangling
  // command reference, which now fails the run loudly instead of fake-succeeding.)
  private static SequenceStep MarkerStep() => new() {
    Order = 0,
    StepId = "marker",
    StepType = SequenceStepType.Action,
    WaitForImage = new WaitForImageConfig { TimeoutMs = 0 }
  };

  private static CommandSequence BuildSelfReschedulingSequence(string id, string name, string expectedMarkerState) {
    var sequence = new CommandSequence { Id = id, Name = name };
    sequence.SetSteps(new[] {
      MarkerStep(),
      new SequenceStep {
        Order = 1,
        StepId = "reschedule",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload {
          Type = ActionTypes.RescheduleSelf,
          Parameters = { ["option"] = "OncePerRun" }
        },
        Condition = new CommandOutcomeStepCondition { StepRef = "marker", ExpectedState = expectedMarkerState }
      }
    });
    return sequence;
  }

  private static async Task SeedAsync(
      IServiceProvider services, CommandSequence sequence, string queueId, bool cycle = false) {
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    template.Entries.Add(new QueueTemplateEntry { SequenceId = sequence.Id, ScheduleType = ScheduleType.OncePerRun });
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);

    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline",
      CycleExecution = cycle, LinkedTemplateId = template.Id
    }).ConfigureAwait(false);
  }

  private static async Task RunToCompletionAsync(IQueueExecutionService engine, string queueId) {
    (await engine.StartAsync(queueId).ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    var sw = Stopwatch.StartNew();
    while (engine.IsRunning(queueId) && sw.ElapsedMilliseconds < 10000) {
      await Task.Delay(20).ConfigureAwait(false);
    }
    engine.IsRunning(queueId).Should().BeFalse();
  }

  private static async Task<ExecutionLogEntry?> GetQueueRunEntryAsync(IExecutionLogService log, string queueId) {
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "queue", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false);
    return page.Items.FirstOrDefault(e => e.ObjectRef.ObjectId == queueId);
  }

  [Fact] // T023 (positive) / T023a — an IF-gated reschedule (forced true) fires the sequence again.
  public async Task IfGatedRescheduleForcedTrueProducesASecondFiring() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient(); // force host build
    var services = app.Services;
    await SeedAsync(services, BuildSelfReschedulingSequence("seq-true", "Daily", "success"), "q-true").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    await RunToCompletionAsync(engine, "q-true").ConfigureAwait(false);

    var entry = await GetQueueRunEntryAsync(services.GetRequiredService<IExecutionLogService>(), "q-true").ConfigureAwait(false);
    entry.Should().NotBeNull();
    entry!.FinalStatus.Should().Be("success");
    // S once-per-run + one self-reschedule firing = 2 executed (FR-007/FR-016).
    entry.Summary.Should().Contain("2 sequence(s) executed");
  }

  [Fact] // T023 (negative) — the IF condition false produces exactly one firing.
  public async Task IfGatedRescheduleForcedFalseProducesNoExtraFiring() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, BuildSelfReschedulingSequence("seq-false", "Daily", "failed"), "q-false").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    await RunToCompletionAsync(engine, "q-false").ConfigureAwait(false);

    var entry = await GetQueueRunEntryAsync(services.GetRequiredService<IExecutionLogService>(), "q-false").ConfigureAwait(false);
    entry.Should().NotBeNull();
    entry!.Summary.Should().Contain("1 sequence(s) executed");
  }

  [Fact] // T022a — two accepted self-reschedules in one run produce two independent extra firings.
  public async Task TwoSelfReschedulesInOneRunProduceTwoFirings() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    // A sequence that reschedules itself twice (two action steps, both forced true).
    var sequence = new CommandSequence { Id = "seq-twice", Name = "Twice" };
    sequence.SetSteps(new[] {
      MarkerStep(),
      new SequenceStep {
        Order = 1, StepId = "r1", StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf, Parameters = { ["option"] = "OncePerRun" } },
        Condition = new CommandOutcomeStepCondition { StepRef = "marker", ExpectedState = "success" }
      },
      new SequenceStep {
        Order = 2, StepId = "r2", StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf, Parameters = { ["option"] = "OncePerRun" } },
        Condition = new CommandOutcomeStepCondition { StepRef = "marker", ExpectedState = "success" }
      }
    });
    await SeedAsync(services, sequence, "q-twice").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    await RunToCompletionAsync(engine, "q-twice").ConfigureAwait(false);

    var entry = await GetQueueRunEntryAsync(services.GetRequiredService<IExecutionLogService>(), "q-twice").ConfigureAwait(false);
    entry.Should().NotBeNull();
    // Original firing + two independent reschedules drained in the same cycle = 3 executed.
    entry!.Summary.Should().Contain("3 sequence(s) executed");
  }

  [Fact] // T048 — the action entry and the resulting firing are visible and attributable in the logs.
  public async Task LogsShowRescheduleDecisionAndTaggedFiring() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedAsync(services, BuildSelfReschedulingSequence("seq-obs", "Observable", "success"), "q-obs").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    await RunToCompletionAsync(engine, "q-obs").ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", PageSize = 200 }).ConfigureAwait(false);
    var firings = page.Items.Where(e => e.ObjectRef.ObjectId == "seq-obs").ToList();
    firings.Should().HaveCountGreaterThanOrEqualTo(2); // original + the rescheduled firing

    // The reschedule decision entry records the option, resolved timing, current-run-only, scheduled.
    var decisionDetail = firings
      .SelectMany(f => f.Details)
      .FirstOrDefault(d => d.Attributes != null
        && d.Attributes.TryGetValue("stepType", out var st) && st as string == "reschedule-self");
    decisionDetail.Should().NotBeNull();
    (decisionDetail!.Attributes!["actionOutcome"] as string).Should().Be("scheduled");
    (decisionDetail.Attributes!["option"] as string).Should().Be("OncePerRun");
    decisionDetail.Attributes!["currentRunOnly"].Should().Be(true);

    // The rescheduled firing is tagged as self-reschedule-originated for attribution (FR-014).
    var taggedFiring = firings.FirstOrDefault(f =>
      f.Details.Any(d => d.Attributes != null
        && d.Attributes.TryGetValue("selfRescheduleOrigin", out var origin) && origin is true));
    taggedFiring.Should().NotBeNull();
  }

  [Fact] // Feature 125 (SC-002): four Timer bookings without keep leave the last booking pending.
  public async Task FourTimerBookingsWithoutKeepLeaveTheLastBookingPending() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var sequence = new CommandSequence { Id = "seq-nokeep-4", Name = "NoKeep4" };
    var steps = new System.Collections.Generic.List<SequenceStep>();
    var order = 0;
    foreach (var offset in new[] { "00:15:00", "00:50:00", "00:30:00", "00:40:00" }) {
      var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
      action.Parameters["option"] = "Timer";
      action.Parameters["timerRelativeOffset"] = offset;
      steps.Add(new SequenceStep { Order = order, StepId = $"r{order}", StepType = SequenceStepType.Action, Action = action });
      order++;
    }
    sequence.SetSteps(steps);
    await SeedAsync(services, sequence, "q-nokeep-4").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    (await engine.StartAsync("q-nokeep-4").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    try {
      var registry = services.GetRequiredService<IQueueRunRegistry>();
      var sw = Stopwatch.StartNew();
      double ahead = 0;
      // A slow CI runner can need more than 30 s to book all four times, so the wait is long.
      while (sw.ElapsedMilliseconds < 90000) {
        if (registry.TryGet("q-nokeep-4", out var handle)
            && handle.SnapshotPendingTimerFirings().FirstOrDefault(e => e.SequenceId == sequence.Id)?.FireAt is { } fireAt) {
          ahead = (fireAt - DateTimeOffset.Now).TotalMinutes;
          if (ahead > 39) break;
        }
        await Task.Delay(50).ConfigureAwait(false);
      }
      await Task.Delay(500).ConfigureAwait(false);
      registry.TryGet("q-nokeep-4", out var finalHandle).Should().BeTrue();
      var pending = finalHandle!.SnapshotPendingTimerFirings().Should().ContainSingle().Subject;
      (pending.FireAt!.Value - DateTimeOffset.Now).TotalMinutes.Should().BeInRange(39, 40.2);
    }
    finally {
      await engine.StopAsync("q-nokeep-4").ConfigureAwait(false);
    }
  }

  // ── Feature 116 (#249): a booked run keeps the parameter scope of the run that booked it ──────
  // The tests do not upload images, so the tap result can be different on different hosts. The
  // tests assert what the log records about the resolved values, not the tap result.

  private const string SlotValue = "pns-nova-option-affinity";
  private const string UnresolvedText = "could not be resolved from any scope";

  /// <summary>The command <c>c116</c>: one tap step that gets its image id from <c>{{slot}}</c>.</summary>
  private static Command SlotTapCommand(string id) {
    var command = new Command { Id = id, Name = "ZZZ.SlotTap116" };
    command.Parameters.Add(new GameBot.Domain.Parameters.ParameterDeclaration { Name = "slot", Required = true });
    command.Steps.Add(new CommandStep {
      Type = CommandStepType.PrimitiveTap,
      Order = 0,
      PrimitiveTap = new PrimitiveTapConfig { DetectionTarget = new DetectionTarget("option-a", 0.85) },
      FieldTemplates = new System.Collections.Generic.Dictionary<string, string> {
        ["primitiveTap.detectionTarget.referenceImageId"] = "{{slot}}"
      }
    });
    return command;
  }

  /// <summary>
  /// A sequence with the required parameter <c>slot</c>. Step 0 books a run with
  /// <paramref name="rescheduleParameters"/>. Step 1 (<c>s1</c>) runs <paramref name="commandId"/>
  /// with the binding <c>slot = {{slot}}</c>.
  /// </summary>
  private static CommandSequence SlotSequence(
      string id, string commandId, params (string Key, string Value)[] rescheduleParameters) {
    var sequence = new CommandSequence { Id = id, Name = $"Seq-{id}" };
    sequence.Parameters.Add(new GameBot.Domain.Parameters.ParameterDeclaration { Name = "slot", Required = true });
    var action = new SequenceActionPayload { Type = ActionTypes.RescheduleSelf };
    foreach (var (key, value) in rescheduleParameters) action.Parameters[key] = value;
    sequence.SetSteps(new[] {
      new SequenceStep { Order = 0, StepId = "r0", StepType = SequenceStepType.Action, Action = action },
      new SequenceStep {
        Order = 1, StepId = "s1", StepType = SequenceStepType.Command, CommandId = commandId,
        ParameterBindings = new System.Collections.ObjectModel.Collection<GameBot.Domain.Parameters.ParameterBinding> {
          new() { Name = "slot", Value = "{{slot}}" }
        }
      }
    });
    return sequence;
  }

  /// <summary>Seeds the command, the sequence, and a queue with no cycling that has one OncePerRun entry with <c>slot</c>.</summary>
  private static async Task SeedSlotQueueAsync(IServiceProvider services, Command command, CommandSequence sequence, string queueId) {
    await services.GetRequiredService<ICommandRepository>().AddAsync(command).ConfigureAwait(false);
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);
    var entry = new QueueTemplateEntry { SequenceId = sequence.Id, ScheduleType = ScheduleType.OncePerRun };
    entry.ParameterValues.Add(new GameBot.Domain.Parameters.ParameterBinding { Name = "slot", Value = SlotValue });
    var template = new QueueTemplate { Id = $"tpl-{queueId}", Name = $"T-{queueId}" };
    template.Entries.Add(entry);
    await services.GetRequiredService<IQueueTemplateRepository>().CreateAsync(template).ConfigureAwait(false);
    await services.GetRequiredService<IQueueRepository>().CreateAsync(new ExecutionQueue {
      Id = queueId, Name = $"Q-{queueId}", EmulatorSerial = "emu-offline", LinkedTemplateId = template.Id
    }).ConfigureAwait(false);
  }

  /// <summary>The <c>parameters</c> items in the command log of <paramref name="commandId"/>.</summary>
  private static async Task<System.Collections.Generic.List<ExecutionDetailItem>> ParametersItemsAsync(
      IExecutionLogService log, string commandId) {
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "command", PageSize = 200 }).ConfigureAwait(false);
    return page.Items
      .Where(e => string.Equals(e.ObjectRef?.ObjectId, commandId, StringComparison.Ordinal))
      .SelectMany(e => e.Details ?? Array.Empty<ExecutionDetailItem>())
      .Where(d => string.Equals(d.Kind, "parameters", StringComparison.Ordinal))
      .ToList();
  }

  /// <summary>Waits until the command log has <paramref name="expected"/> <c>parameters</c> items, or until the timeout.</summary>
  private static async Task<System.Collections.Generic.List<ExecutionDetailItem>> WaitForParametersItemsAsync(
      IExecutionLogService log, string commandId, int expected, int timeoutMs) {
    var sw = Stopwatch.StartNew();
    while (true) {
      var items = await ParametersItemsAsync(log, commandId).ConfigureAwait(false);
      if (items.Count >= expected || sw.ElapsedMilliseconds > timeoutMs) return items;
      await Task.Delay(50).ConfigureAwait(false);
    }
  }

  /// <summary>The <c>resolvedParameters</c> of one <c>parameters</c> item, as (name, value, originLayer).</summary>
  private static System.Collections.Generic.List<(string Name, string Value, string Layer)> Resolved(ExecutionDetailItem item) {
    object? raw = null;
    item.Attributes?.TryGetValue("resolvedParameters", out raw);
    var text = raw switch {
      System.Text.Json.JsonElement element => element.ValueKind == System.Text.Json.JsonValueKind.String ? element.GetString() : element.ToString(),
      string s => s,
      null => null,
      _ => System.Text.Json.JsonSerializer.Serialize(raw)
    };
    text.Should().NotBeNull();
    using var document = System.Text.Json.JsonDocument.Parse(text!);
    return document.RootElement.EnumerateArray()
      .Select(p => (p.GetProperty("name").GetString()!, p.GetProperty("value").GetString()!, p.GetProperty("originLayer").GetString()!))
      .ToList();
  }

  /// <summary>Asserts that no log entry of <paramref name="sequenceId"/> has the "could not be resolved" error.</summary>
  private static async Task ShouldHaveNoUnresolvedErrorAsync(IExecutionLogService log, string sequenceId) {
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", PageSize = 200 }).ConfigureAwait(false);
    var entries = page.Items.Where(e => string.Equals(e.ObjectRef?.ObjectId, sequenceId, StringComparison.Ordinal)).ToList();
    entries.Should().NotBeEmpty();
    System.Text.Json.JsonSerializer.Serialize(entries).Should().NotContain(UnresolvedText);
  }

  [Fact] // T006 (feature 116) — issue #249: a OncePerRun booked run resolves the entry value on the real dispatch path.
  public async Task OncePerRunBookedRunResolvesTheEntryValue() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedSlotQueueAsync(services, SlotTapCommand("c116-once"),
      SlotSequence("s116-once", "c116-once", ("option", "OncePerRun")), "q116-once").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    await RunToCompletionAsync(engine, "q116-once").ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var entry = await GetQueueRunEntryAsync(log, "q116-once").ConfigureAwait(false);
    entry.Should().NotBeNull();
    entry!.Summary.Should().Contain("2 sequence(s) executed");

    var items = await WaitForParametersItemsAsync(log, "c116-once", 2, 30000).ConfigureAwait(false);
    items.Should().HaveCount(2, "the entry run and the booked run must both run the command");
    foreach (var item in items) {
      Resolved(item).Should().Equal(("slot", SlotValue, GameBot.Domain.Parameters.ParameterScopeLayers.Entry));
    }
    await ShouldHaveNoUnresolvedErrorAsync(log, "s116-once").ConfigureAwait(false);
  }

  [Fact] // T007 (feature 116) — a Timer booking chain resolves the entry value on the real dispatch path (real clock).
  public async Task TimerBookedRunsResolveTheEntryValueForEachGeneration() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;
    await SeedSlotQueueAsync(services, SlotTapCommand("c116-timer"),
      SlotSequence("s116-timer", "c116-timer", ("option", "Timer"), ("timerRelativeOffset", "00:00:01")), "q116-timer").ConfigureAwait(false);

    var engine = services.GetRequiredService<IQueueExecutionService>();
    var log = services.GetRequiredService<IExecutionLogService>();
    (await engine.StartAsync("q116-timer").ConfigureAwait(false)).Should().Be(QueueStartOutcome.Started);
    System.Collections.Generic.List<ExecutionDetailItem> items;
    try {
      // The real clock runs the 1-second bookings. The wait is long, so that a slow CI runner does not fail the test.
      items = await WaitForParametersItemsAsync(log, "c116-timer", 3, 60000).ConfigureAwait(false);
    }
    finally {
      await engine.StopAsync("q116-timer").ConfigureAwait(false);
    }

    items.Should().HaveCountGreaterThanOrEqualTo(3, "the entry run and two booked runs must run the command");
    foreach (var item in items) {
      Resolved(item).Should().Equal(("slot", SlotValue, GameBot.Domain.Parameters.ParameterScopeLayers.Entry));
    }
    await ShouldHaveNoUnresolvedErrorAsync(log, "s116-timer").ConfigureAwait(false);
  }
}
