using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using GameBot.Service.Services.SequenceExecution;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.Sequences;

/// <summary>
/// A sequence's execution-log entry is opened before its first step and closed after its last. When a
/// run unwound in between — overwhelmingly the queue's per-sequence watchdog cancelling a firing that
/// overran — the close was skipped and the entry read "running" forever. Those orphans are
/// indistinguishable from a sequence still going, so a queue could look busy while doing nothing.
/// </summary>
[Collection("ConfigIsolation")]
public sealed class AbandonedSequenceLogIntegrationTests {
  public AbandonedSequenceLogIntegrationTests() {
    Environment.SetEnvironmentVariable("GAMEBOT_USE_ADB", "false");
    Environment.SetEnvironmentVariable("GAMEBOT_AUTH_TOKEN", "test-token");
    TestEnvironment.PrepareCleanDataDir();
  }

  private static CommandSequence SlowSequence(string id) {
    var sequence = new CommandSequence { Id = id, Name = id };
    sequence.SetSteps(new[] {
      // A targetless wait is a plain delay, so this is a step that reliably outlives the token below
      // without needing an emulator.
      new SequenceStep {
        Order = 0,
        StepId = "slow",
        StepType = SequenceStepType.Action,
        WaitForImage = new WaitForImageConfig { TimeoutMs = 60_000 }
      }
    });
    return sequence;
  }

  [Fact]
  public async Task CancelledRunClosesItsEntryInsteadOfLeavingItRunning() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    await services.GetRequiredService<ISequenceRepository>()
      .CreateAsync(SlowSequence("seq-cancelled")).ConfigureAwait(false);

    using var cts = new CancellationTokenSource();
    cts.CancelAfter(TimeSpan.FromMilliseconds(200));

    var execution = services.GetRequiredService<ISequenceExecutionService>();
    var act = async () => await execution
      .ExecuteAsync("seq-cancelled", sessionId: null, parentContext: null, cts.Token)
      .ConfigureAwait(false);

    // The cancellation still propagates — the caller (a queue run) must keep seeing it.
    await act.Should().ThrowAsync<OperationCanceledException>().ConfigureAwait(false);

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(
      new ExecutionLogQuery { ObjectType = "sequence", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false);
    var entry = page.Items.FirstOrDefault(e => e.ObjectRef.ObjectId == "seq-cancelled");

    entry.Should().NotBeNull("a cancelled run must still leave a closed entry behind");
    entry!.FinalStatus.Should().NotBe("running", "an entry left open is indistinguishable from a live run");
    // The log has only success/running/failure, so an aborted run is a failure and the summary
    // carries the distinction between "cancelled" and "faulted".
    entry.FinalStatus.Should().Be("failure");
    entry.Summary.Should().Contain("cancelled");
  }

  [Fact] // A normal run is untouched by the abort path.
  public async Task CompletedRunStillRecordsItsOwnStatus() {
    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var services = app.Services;

    var sequence = new CommandSequence { Id = "seq-normal", Name = "seq-normal" };
    sequence.SetSteps(new[] {
      new SequenceStep {
        Order = 0,
        StepId = "quick",
        StepType = SequenceStepType.Action,
        WaitForImage = new WaitForImageConfig { TimeoutMs = 0 }
      }
    });
    await services.GetRequiredService<ISequenceRepository>().CreateAsync(sequence).ConfigureAwait(false);

    var result = await services.GetRequiredService<ISequenceExecutionService>()
      .ExecuteAsync("seq-normal", sessionId: null, parentContext: null).ConfigureAwait(false);
    result.Status.Should().Be("Succeeded");

    var log = services.GetRequiredService<IExecutionLogService>();
    var page = await log.QueryAsync(
      new ExecutionLogQuery { ObjectType = "sequence", RootsOnly = true, PageSize = 100 }).ConfigureAwait(false);
    page.Items.FirstOrDefault(e => e.ObjectRef.ObjectId == "seq-normal")!.FinalStatus.Should().Be("success");
  }
}
