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

  // Builds sequence S = [ marker command (always succeeds in stub mode), reschedule-self OncePerRun
  // gated on the marker's outcome ]. expectedState selects whether the IF branch fires.
  private static CommandSequence BuildSelfReschedulingSequence(string id, string name, string expectedMarkerState) {
    var sequence = new CommandSequence { Id = id, Name = name };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "marker",
        CommandId = "cmd-marker",
        StepType = SequenceStepType.Command
      },
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
      new SequenceStep { Order = 0, StepId = "marker", CommandId = "cmd-marker", StepType = SequenceStepType.Command },
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
}
