using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 198 (B-044, FR-005, SC-003): the subtree of a queue log has no limit on the number of children.
/// </summary>
public sealed class ExecutionLogQueueSubtreeSizeTests {
  [Fact]
  public async Task QueueSubtreeWith500ChildrenGivesAllChildrenInSequenceOrder() {
    var storageRoot = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(storageRoot);
    using var repository = new FileExecutionLogRepository(storageRoot);
    using var retention = new ExecutionLogRetentionPolicyRepository(storageRoot);
    var service = new ExecutionLogService(repository, retention);

    const string rootId = "queue-root";
    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(new ExecutionLogEntry {
      Id = rootId,
      TimestampUtc = now,
      ExecutionType = "queue",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("queue", "q-1", "Queue"),
      Navigation = new ExecutionNavigationContext("/authoring/queues/q-1", null),
      Hierarchy = new ExecutionHierarchyContext(rootId, null, 0, null),
      RetentionExpiresUtc = now.AddDays(30)
    }).ConfigureAwait(false);

    // Write the children in reverse order, so that the test also checks the sort.
    for (var i = 500; i >= 1; i--) {
      await repository.AddAsync(new ExecutionLogEntry {
        Id = $"child-{i:D3}",
        TimestampUtc = now.AddSeconds(i),
        ExecutionType = "sequence",
        FinalStatus = "success",
        ObjectRef = new ExecutionObjectReference("sequence", "s-1", "Sequence"),
        Navigation = new ExecutionNavigationContext("/authoring/sequences/s-1", null),
        Hierarchy = new ExecutionHierarchyContext(rootId, rootId, 1, i),
        RetentionExpiresUtc = now.AddDays(30)
      }).ConfigureAwait(false);
    }

    var subtree = await service.GetSubtreeAsync(rootId).ConfigureAwait(false);

    subtree.Should().NotBeNull();
    subtree!.Root.Children.Should().HaveCount(500);
    subtree.Root.Children.Select(c => c.Order).Should().Equal(Enumerable.Range(1, 500));
  }
}
