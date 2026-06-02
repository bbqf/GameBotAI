using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class SequenceGroupingIntegrationTests {
  private static readonly string[] CommandNames = { "Command A", "Command B" };

  [Fact]
  public async Task SequenceRunProducesSingleRootWithLinkedChildrenAndNestedSubtree() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var logService = app.Services.GetRequiredService<IExecutionLogService>();

    var rootId = await logService.LogSequenceStartAsync("seq-1", "My Sequence").ConfigureAwait(false);

    await logService.LogCommandExecutionAsync(
      "cmd-a", "Command A", "success",
      new[] { new PrimitiveTapStepOutcome(1, "executed", null, new PrimitiveTapResolvedPoint(10, 20), 0.9) },
      ChildContext(rootId, 1)).ConfigureAwait(false);
    await logService.LogCommandExecutionAsync(
      "cmd-b", "Command B", "success",
      Array.Empty<PrimitiveTapStepOutcome>(),
      ChildContext(rootId, 2)).ConfigureAwait(false);

    var details = new List<ExecutionDetailItem> {
      StepDetail(1, "cmd-a", "Command A"),
      StepDetail(2, "cmd-b", "Command B")
    };
    await logService.LogSequenceFinalizeAsync(
      rootId, "seq-1", "My Sequence", "success",
      "Sequence 'My Sequence' success with 2 steps executed.",
      new ExecutionLogContext { Depth = 0, SequenceId = "seq-1", SequenceLabel = "My Sequence" },
      details).ConfigureAwait(false);

    // Roots-only list returns ONLY the sequence — invoked commands are excluded.
    var roots = await logService.QueryAsync(new ExecutionLogQuery { RootsOnly = true, PageSize = 50 }).ConfigureAwait(false);
    roots.Items.Should().ContainSingle();
    roots.Items[0].Id.Should().Be(rootId);
    roots.Items[0].FinalStatus.Should().Be("success");

    // But the child records are kept (linked), not deleted.
    var all = await logService.QueryAsync(new ExecutionLogQuery { RootsOnly = false, PageSize = 50 }).ConfigureAwait(false);
    all.Items.Should().HaveCount(3);

    // The subtree nests the invoked commands under the sequence.
    var subtree = await logService.GetSubtreeAsync(rootId).ConfigureAwait(false);
    subtree.Should().NotBeNull();
    subtree!.Root.ExecutionId.Should().Be(rootId);
    subtree.Root.Children.Should().HaveCount(2);
    subtree.Root.Children.Select(c => c.CommandName).Should().BeEquivalentTo(CommandNames);
    subtree.Root.Children.Should().OnlyContain(c => c.ExecutionId != null);
    // The command with a primitive tap exposes it as a nested child.
    subtree.Root.Children.Single(c => c.CommandName == "Command A").Children.Should().NotBeEmpty();
  }

  private static ExecutionLogContext ChildContext(string rootId, int sequenceIndex)
    => new() {
      ParentExecutionId = rootId,
      RootExecutionId = rootId,
      Depth = 1,
      SequenceIndex = sequenceIndex,
      SequenceId = "seq-1",
      SequenceLabel = "My Sequence"
    };

  private static ExecutionDetailItem StepDetail(int order, string commandId, string commandName)
    => new(
      "step",
      $"Step ran command '{commandName}'.",
      new Dictionary<string, object?> {
        ["stepOrder"] = order,
        ["stepType"] = "command",
        ["status"] = "executed",
        ["actionOutcome"] = "executed",
        ["commandId"] = commandId,
        ["commandName"] = commandName,
        ["sequenceId"] = "seq-1",
        ["sequenceLabel"] = "My Sequence",
        ["stepId"] = $"step-{order}",
        ["stepLabel"] = $"Step {order}"
      },
      "normal");
}
