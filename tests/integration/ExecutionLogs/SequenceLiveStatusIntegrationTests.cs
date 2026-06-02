using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class SequenceLiveStatusIntegrationTests {
  [Fact]
  public async Task RunningRootIsSurfacedThenFinalizedInPlaceWithoutDuplicate() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var svc = app.Services.GetRequiredService<IExecutionLogService>();

    // Start: a single in-progress root entry is visible while the sequence runs.
    var rootId = await svc.LogSequenceStartAsync("seq-live", "Live Sequence").ConfigureAwait(false);

    var running = await svc.QueryAsync(new ExecutionLogQuery { RootsOnly = true, PageSize = 50 }).ConfigureAwait(false);
    running.Items.Should().ContainSingle();
    running.Items[0].Id.Should().Be(rootId);
    running.Items[0].FinalStatus.Should().Be("running");

    var runningSubtree = await svc.GetSubtreeAsync(rootId).ConfigureAwait(false);
    runningSubtree!.FinalStatus.Should().Be("running");

    // Children execute and link to the running root.
    await svc.LogCommandExecutionAsync("cmd-a", "Command A", "success", Array.Empty<PrimitiveTapStepOutcome>(),
      new ExecutionLogContext { ParentExecutionId = rootId, RootExecutionId = rootId, Depth = 1, SequenceIndex = 1, SequenceId = "seq-live", SequenceLabel = "Live Sequence" }).ConfigureAwait(false);

    // Finalize: the SAME entry settles to its terminal status — no duplicate root row.
    await svc.LogSequenceFinalizeAsync(rootId, "seq-live", "Live Sequence", "success",
      "Sequence 'Live Sequence' success with 1 step executed.",
      new ExecutionLogContext { Depth = 0, SequenceId = "seq-live", SequenceLabel = "Live Sequence" },
      new List<ExecutionDetailItem> {
        new("step", "Step ran command 'Command A'.", new Dictionary<string, object?> {
          ["stepOrder"] = 1, ["stepType"] = "command", ["status"] = "executed", ["actionOutcome"] = "executed",
          ["commandId"] = "cmd-a", ["commandName"] = "Command A", ["sequenceId"] = "seq-live", ["sequenceLabel"] = "Live Sequence",
          ["stepId"] = "step-1", ["stepLabel"] = "Step 1"
        }, "normal")
      }).ConfigureAwait(false);

    var finalized = await svc.QueryAsync(new ExecutionLogQuery { RootsOnly = true, PageSize = 50 }).ConfigureAwait(false);
    finalized.Items.Should().ContainSingle("finalize must replace the in-progress entry, not add a second root");
    finalized.Items[0].Id.Should().Be(rootId);
    finalized.Items[0].FinalStatus.Should().Be("success");

    var all = await svc.QueryAsync(new ExecutionLogQuery { RootsOnly = false, PageSize = 50 }).ConfigureAwait(false);
    all.Items.Should().HaveCount(2, "one root + one linked child");
  }
}
