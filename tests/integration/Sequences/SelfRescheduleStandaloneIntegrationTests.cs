using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.QueueExecution;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// Feature 065 (US3): a sequence containing a self-reschedule action runs correctly outside any
/// queue — the action is a success no-op, schedules nothing, and the remaining steps still run
/// (FR-011, FR-012). The standalone execute path never sets an originating queue (FR-011 regression).
/// </summary>
[Collection("ConfigIsolation")]
public sealed class SelfRescheduleStandaloneIntegrationTests {
  public SelfRescheduleStandaloneIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  [Fact]
  public async Task StandaloneRunRecordsSuccessNoOpAndSchedulesNothing() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var sequence = new CommandSequence { Id = "seq-standalone", Name = "Standalone" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "reschedule",
        StepType = SequenceStepType.Action,
        Action = new SequenceActionPayload {
          Type = ActionTypes.RescheduleSelf,
          Parameters = { ["option"] = "OncePerRun" }
        }
      },
      new SequenceStep { Order = 1, StepId = "after", CommandId = "cmd-after", StepType = SequenceStepType.Command }
    });
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var execution = services.GetRequiredService<ISequenceExecutionService>();
    // No parent context ⇒ standalone (not started from a queue), OriginatingQueueId stays unset.
    var result = await execution.ExecuteAsync("seq-standalone", sessionId: null, parentContext: null).ConfigureAwait(false);

    // The action is a no-op success and the sequence completes normally (the following step still runs).
    result.Status.Should().Be("Succeeded");

    // Nothing was scheduled anywhere — no queue run exists.
    services.GetRequiredService<IQueueRunRegistry>().IsRunning("seq-standalone").Should().BeFalse();

    // The execution log records the action as a success no-op with the "not started from a queue" reason.
    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(new ExecutionLogQuery { ObjectType = "sequence", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false);
    var entry = page.Items.FirstOrDefault(e => e.ObjectRef.ObjectId == "seq-standalone");
    entry.Should().NotBeNull();
    entry!.FinalStatus.Should().Be("success");
    var rescheduleDetail = entry.Details.FirstOrDefault(d =>
      d.Attributes != null
      && d.Attributes.TryGetValue("stepType", out var st)
      && st as string == "reschedule-self");
    rescheduleDetail.Should().NotBeNull();
    (rescheduleDetail!.Attributes!["actionOutcome"] as string).Should().Be("noop");
    rescheduleDetail.Message.Should().Contain("no originating queue");
  }
}
