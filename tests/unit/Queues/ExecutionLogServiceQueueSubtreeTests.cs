using System.Collections.Generic;
using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.Queues;

/// <summary>
/// A queue run is a top-level execution-log entry whose children are the executed sequences.
/// Because the queue root carries no step outcomes, the subtree builder nests its direct children
/// (the sequences) — so a queue lists as a single root row with the sequences underneath (FR-007).
/// </summary>
public sealed class ExecutionLogServiceQueueSubtreeTests {
  private static ExecutionLogEntry Entry(string id, string type, string? parentId, string rootId, int? sequenceIndex) => new() {
    Id = id,
    ExecutionType = type,
    FinalStatus = "success",
    ObjectRef = new ExecutionObjectReference(type, id, $"{type}-{id}"),
    Navigation = new ExecutionNavigationContext($"/authoring/{type}/{id}", null),
    Hierarchy = new ExecutionHierarchyContext(rootId, parentId, parentId is null ? 0 : 1, sequenceIndex),
    Summary = $"{type} {id}"
  };

  [Fact]
  public void QueueRootNestsItsSequenceChildren() {
    var queueRoot = Entry("qr", "queue", parentId: null, rootId: "qr", sequenceIndex: null);
    var seq1 = Entry("s1", "sequence", parentId: "qr", rootId: "qr", sequenceIndex: 1);
    var seq2 = Entry("s2", "sequence", parentId: "qr", rootId: "qr", sequenceIndex: 2);
    var all = new List<ExecutionLogEntry> { queueRoot, seq1, seq2 };

    var subtree = ExecutionLogService.BuildSubtree(queueRoot, all);

    subtree.Root.NodeKind.Should().Be("queue");
    subtree.Root.Children.Should().HaveCount(2);
    subtree.Root.Children[0].NodeKind.Should().Be("sequence");
    subtree.Root.Children[0].ExecutionId.Should().Be("s1");
    subtree.Root.Children[1].ExecutionId.Should().Be("s2");
  }
}
