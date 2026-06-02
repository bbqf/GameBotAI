using FluentAssertions;
using GameBot.Domain.Logging;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ExecutionLogRepositoryHierarchyTests {
  private static readonly string[] AllEntryIds = { "root-1", "child-1", "child-2" };
  private static readonly string[] Root1SubtreeIds = { "root-1", "child-1", "child-2" };

  [Fact]
  public async Task UpsertAsyncReplacesExistingEntryWithoutDuplicate() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateRoot("root-1", now, "Sequence A", "running")).ConfigureAwait(false);

    // Prime the in-memory cache before upserting.
    var running = await repository.QueryAsync(new ExecutionLogQuery { PageSize = 50 }).ConfigureAwait(false);
    running.Items.Should().ContainSingle().Which.FinalStatus.Should().Be("running");

    await repository.UpsertAsync(CreateRoot("root-1", now, "Sequence A", "success")).ConfigureAwait(false);

    var finalized = await repository.QueryAsync(new ExecutionLogQuery { PageSize = 50 }).ConfigureAwait(false);
    finalized.Items.Should().ContainSingle("upsert must replace, not duplicate");
    finalized.Items[0].FinalStatus.Should().Be("success");
  }

  [Fact]
  public async Task QueryRootsOnlyExcludesChildExecutions() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateRoot("root-1", now, "Sequence A", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateChild("child-1", now.AddSeconds(1), "Command One", "root-1", 1)).ConfigureAwait(false);
    await repository.AddAsync(CreateChild("child-2", now.AddSeconds(2), "Command Two", "root-1", 2)).ConfigureAwait(false);

    var rootsOnly = await repository.QueryAsync(new ExecutionLogQuery { RootsOnly = true, PageSize = 50 }).ConfigureAwait(false);
    rootsOnly.Items.Should().ContainSingle().Which.Id.Should().Be("root-1");

    var all = await repository.QueryAsync(new ExecutionLogQuery { RootsOnly = false, PageSize = 50 }).ConfigureAwait(false);
    all.Items.Select(i => i.Id).Should().BeEquivalentTo(AllEntryIds);
  }

  [Fact]
  public async Task GetSubtreeReturnsRootAndDescendantsOnly() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateRoot("root-1", now, "Sequence A", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateChild("child-1", now.AddSeconds(1), "Command One", "root-1", 1)).ConfigureAwait(false);
    await repository.AddAsync(CreateChild("child-2", now.AddSeconds(2), "Command Two", "root-1", 2)).ConfigureAwait(false);
    // Unrelated root + child that must not leak into the subtree.
    await repository.AddAsync(CreateRoot("root-2", now, "Sequence B", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateChild("child-3", now.AddSeconds(1), "Other", "root-2", 1)).ConfigureAwait(false);

    var subtree = await repository.GetSubtreeAsync("root-1").ConfigureAwait(false);

    subtree.Select(e => e.Id).Should().BeEquivalentTo(Root1SubtreeIds);
  }

  [Fact]
  public async Task GetSubtreeReturnsEmptyForUnknownRoot() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var subtree = await repository.GetSubtreeAsync("does-not-exist").ConfigureAwait(false);

    subtree.Should().BeEmpty();
  }

  private static ExecutionLogEntry CreateRoot(string id, DateTimeOffset timestampUtc, string objectName, string status)
    => new() {
      Id = id,
      TimestampUtc = timestampUtc,
      ExecutionType = "sequence",
      FinalStatus = status,
      ObjectRef = new ExecutionObjectReference("sequence", id, objectName),
      Navigation = new ExecutionNavigationContext($"/authoring/sequences/{id}", null),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
      Summary = "Summary",
      RetentionExpiresUtc = timestampUtc.AddDays(30)
    };

  private static ExecutionLogEntry CreateChild(string id, DateTimeOffset timestampUtc, string objectName, string rootId, int sequenceIndex)
    => new() {
      Id = id,
      TimestampUtc = timestampUtc,
      ExecutionType = "command",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("command", id, objectName),
      Navigation = new ExecutionNavigationContext($"/authoring/commands/{id}", $"/authoring/sequences/{rootId}"),
      Hierarchy = new ExecutionHierarchyContext(rootId, rootId, 1, sequenceIndex),
      Summary = "Summary",
      RetentionExpiresUtc = timestampUtc.AddDays(30)
    };

  private static string CreateTempStorageRoot() {
    var root = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }
}
