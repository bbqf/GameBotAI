using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ExecutionSubtreeProjectionTests {
  private static readonly string[] CommandLabels = { "Open Mail", "Collect" };

  [Fact]
  public void BuildSubtreeCorrelatesCommandStepsToChildEntriesByCommandId() {
    var root = SequenceEntry("root-1", "My Sequence", "success", new[] {
      CommandStep(1, "cmd-a", "Open Mail"),
      CommandStep(2, "cmd-b", "Collect")
    });
    var childA = CommandChild("child-a", "cmd-a", "Open Mail", "root-1", 1);
    var childB = CommandChild("child-b", "cmd-b", "Collect", "root-1", 2);

    var subtree = ExecutionLogService.BuildSubtree(root, new[] { root, childA, childB });

    subtree.FinalStatus.Should().Be("success");
    subtree.Root.Children.Should().HaveCount(2);
    subtree.Root.Children.Select(c => c.Label).Should().BeEquivalentTo(CommandLabels);
    subtree.Root.Children.Should().OnlyContain(c => c.ExecutionId != null);
    // Command nodes carry their child execution's own primitive outcomes as nested children.
    subtree.Root.Children[0].Children.Should().NotBeEmpty();
  }

  [Fact]
  public void BuildSubtreePreservesInlineConditionAndWaitSteps() {
    var root = SequenceEntry("root-2", "Conditional", "success", new[] {
      ConditionStep(1),
      WaitStep(2)
    });

    var subtree = ExecutionLogService.BuildSubtree(root, new[] { root });

    subtree.Root.Children.Should().HaveCount(2);
    subtree.Root.Children[0].NodeKind.Should().Be("condition");
    subtree.Root.Children[0].ConditionTrace.Should().NotBeNull();
    subtree.Root.Children[1].NodeKind.Should().Be("wait");
    subtree.Root.Children[1].DetailAttributes.Should().NotBeNull();
  }

  [Fact]
  public void BuildSubtreeNestsChildSequenceWithoutExtraTopLevelNode() {
    // Sequence-invoking-sequence: a child entry that is itself a sequence with its own command child.
    var root = SequenceEntry("root-3", "Outer", "success", new[] { CommandStep(1, "seq-inner", "Inner") });
    var innerSeqLinked = LinkAsChild(SequenceEntry("child-inner", "Inner", "success", new[] { CommandStep(1, "cmd-x", "Tap") }), "seq-inner", "root-3", 1);
    var grandChildLinked = ReParent(CommandChild("gc-1", "cmd-x", "Tap", "root-3", 1), "child-inner");

    var subtree = ExecutionLogService.BuildSubtree(root, new[] { root, innerSeqLinked, grandChildLinked });

    subtree.Root.Children.Should().ContainSingle();
    var innerNode = subtree.Root.Children[0];
    innerNode.NodeKind.Should().Be("sequence");
    innerNode.ExecutionId.Should().Be("child-inner");
    innerNode.Children.Should().ContainSingle();
    innerNode.Children[0].NodeKind.Should().Be("command");
  }

  private static ExecutionLogEntry SequenceEntry(string id, string name, string status, ExecutionStepOutcome[] steps)
    => new() {
      Id = id,
      ExecutionType = "sequence",
      FinalStatus = status,
      ObjectRef = new ExecutionObjectReference("sequence", id, name),
      Navigation = new ExecutionNavigationContext($"/authoring/sequences/{id}", null),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
      Summary = $"{name} executed.",
      StepOutcomes = steps,
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

  private static ExecutionStepOutcome CommandStep(int order, string commandId, string commandName)
    => new(order, "command", "executed", "executed", $"Step ran command '{commandName}'.", "seq", $"step-{order}", "Seq", $"Step {order}") {
      CommandName = commandName,
      CommandId = commandId
    };

  private static ExecutionStepOutcome ConditionStep(int order)
    => new(order, "condition", "executed", "executed", "Condition evaluated to true.", "seq", $"step-{order}", "Seq", $"Step {order}",
      new ConditionEvaluationTrace(true, "true", null, Array.Empty<Dictionary<string, object?>>(), Array.Empty<Dictionary<string, object?>>()));

  private static ExecutionStepOutcome WaitStep(int order)
    => new(order, "waitForImage", "image_detected", "image_detected", "Wait completed.", "seq", $"step-{order}", "Seq", $"Step {order}") {
      DetailAttributes = new WaitForImageDetailAttributes(5000, 5000, "img-1", 0.8, "image_detected", "loaded")
    };

  private static ExecutionLogEntry CommandChild(string id, string commandId, string commandName, string rootId, int sequenceIndex)
    => new() {
      Id = id,
      ExecutionType = "command",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("command", commandId, commandName),
      Navigation = new ExecutionNavigationContext($"/authoring/commands/{commandId}", $"/authoring/sequences/{rootId}"),
      Hierarchy = new ExecutionHierarchyContext(rootId, rootId, 1, sequenceIndex),
      Summary = $"{commandName} executed.",
      StepOutcomes = new[] {
        new ExecutionStepOutcome(1, "primitiveTap", "executed", "executed", "Tap executed.")
      },
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(1)
    };

  private static ExecutionLogEntry LinkAsChild(ExecutionLogEntry entry, string commandId, string rootId, int sequenceIndex)
    => new() {
      Id = entry.Id,
      ExecutionType = entry.ExecutionType,
      FinalStatus = entry.FinalStatus,
      ObjectRef = new ExecutionObjectReference(entry.ObjectRef.ObjectType, commandId, entry.ObjectRef.DisplayNameSnapshot),
      Navigation = entry.Navigation,
      Hierarchy = new ExecutionHierarchyContext(rootId, rootId, 1, sequenceIndex),
      Summary = entry.Summary,
      StepOutcomes = entry.StepOutcomes,
      RetentionExpiresUtc = entry.RetentionExpiresUtc
    };

  private static ExecutionLogEntry ReParent(ExecutionLogEntry entry, string parentId)
    => new() {
      Id = entry.Id,
      ExecutionType = entry.ExecutionType,
      FinalStatus = entry.FinalStatus,
      ObjectRef = entry.ObjectRef,
      Navigation = entry.Navigation,
      Hierarchy = new ExecutionHierarchyContext("root-3", parentId, 2, entry.Hierarchy.SequenceIndex),
      Summary = entry.Summary,
      StepOutcomes = entry.StepOutcomes,
      RetentionExpiresUtc = entry.RetentionExpiresUtc
    };
}
