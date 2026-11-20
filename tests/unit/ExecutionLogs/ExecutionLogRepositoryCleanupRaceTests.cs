using FluentAssertions;
using GameBot.Domain.Logging;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 198 (B-044, FR-001 to FR-004): a write during the retention cleanup must stay in the in-memory list.
/// </summary>
public sealed class ExecutionLogRepositoryCleanupRaceTests {
  private static readonly TimeSpan WriteWait = TimeSpan.FromMilliseconds(200);

  [Fact]
  public async Task AddDuringCleanupRemoveStepIsNotLost() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);
    var now = DateTimeOffset.UtcNow;
    var expiredIds = await AddExpiredEntriesAsync(repository, 1000, now).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("old-root", "queue", "q-1", now.AddHours(-1), "running")).ConfigureAwait(false);
    await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);

    var gate = new HookGate();
    repository.RemoveStepHookForTests = gate.HookAsync;

    var cleanup = repository.DeleteExpiredAsync(DateTimeOffset.UtcNow);
    await gate.Entered.ConfigureAwait(false);
    var add = repository.AddAsync(CreateEntry("new-root", "queue", "q-1", now, "running"));
    var upsert = repository.UpsertAsync(CreateEntry("old-root", "queue", "q-1", now.AddHours(-1), "success"));
    await Task.Delay(WriteWait).ConfigureAwait(false);
    gate.Release();
    var deleted = await cleanup.ConfigureAwait(false);
    await Task.WhenAll(add, upsert).ConfigureAwait(false);

    deleted.Should().Be(1000);
    var roots = await repository.QueryAsync(new ExecutionLogQuery { ObjectId = "q-1", RootsOnly = true }).ConfigureAwait(false);
    roots.Items.Select(i => i.Id).Should().Contain("new-root");
    var oldRoot = await repository.GetSubtreeAsync("old-root").ConfigureAwait(false);
    oldRoot.Should().ContainSingle().Which.FinalStatus.Should().Be("success");
    var all = await repository.QueryAsync(new ExecutionLogQuery { PageSize = 200 }).ConfigureAwait(false);
    all.Items.Select(i => i.Id).Should().NotIntersectWith(expiredIds);
  }

  [Fact]
  public async Task RootAndChildrenAddedDuringCleanupAreReadable() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);
    var now = DateTimeOffset.UtcNow;
    await AddExpiredEntriesAsync(repository, 1000, now).ConfigureAwait(false);
    await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);

    var gate = new HookGate();
    repository.RemoveStepHookForTests = gate.HookAsync;

    var cleanup = repository.DeleteExpiredAsync(DateTimeOffset.UtcNow);
    await gate.Entered.ConfigureAwait(false);
    var writes = new List<Task> { repository.AddAsync(CreateEntry("run-root", "queue", "q-2", now, "running")) };
    for (var i = 1; i <= 10; i++) {
      writes.Add(repository.AddAsync(CreateChild($"run-child-{i}", "run-root", now.AddSeconds(i), i)));
    }
    await Task.Delay(WriteWait).ConfigureAwait(false);
    gate.Release();
    await cleanup.ConfigureAwait(false);
    await Task.WhenAll(writes).ConfigureAwait(false);

    var roots = await repository.QueryAsync(new ExecutionLogQuery { ObjectId = "q-2", RootsOnly = true }).ConfigureAwait(false);
    roots.Items.Select(i => i.Id).Should().Contain("run-root");
    var subtree = await repository.GetSubtreeAsync("run-root").ConfigureAwait(false);
    subtree.Should().HaveCount(11);
  }

  [Fact]
  public async Task CleanupWithNoExpiredEntriesKeepsConcurrentAdd() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);
    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateEntry("live-1", "command", "c-1", now.AddMinutes(-1), "success")).ConfigureAwait(false);
    await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);

    var gate = new HookGate();
    repository.RemoveStepHookForTests = gate.HookAsync;

    var cleanup = repository.DeleteExpiredAsync(DateTimeOffset.UtcNow);
    await gate.Entered.ConfigureAwait(false);
    var add = repository.AddAsync(CreateEntry("live-2", "command", "c-2", now, "success"));
    await Task.Delay(WriteWait).ConfigureAwait(false);
    gate.Release();
    var deleted = await cleanup.ConfigureAwait(false);
    await add.ConfigureAwait(false);

    deleted.Should().Be(0);
    var all = await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);
    all.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { "live-1", "live-2" });
  }

  [Fact]
  public async Task ExpiredEntriesAreRemovedFromListAndDisk() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);
    var now = DateTimeOffset.UtcNow;
    var expiredIds = await AddExpiredEntriesAsync(repository, 5, now).ConfigureAwait(false);
    var liveIds = new List<string>();
    for (var i = 0; i < 5; i++) {
      var id = $"live-{i}";
      liveIds.Add(id);
      await repository.AddAsync(CreateEntry(id, "command", id, now.AddMinutes(-i), "success")).ConfigureAwait(false);
    }

    var deleted = await repository.DeleteExpiredAsync(DateTimeOffset.UtcNow).ConfigureAwait(false);

    deleted.Should().Be(5);
    var dir = Path.Combine(storageRoot, "execution-logs");
    foreach (var id in expiredIds) {
      File.Exists(Path.Combine(dir, id + ".json")).Should().BeFalse();
    }
    var all = await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);
    all.Items.Select(i => i.Id).Should().BeEquivalentTo(liveIds);
  }

  [Fact]
  public async Task AddDuringFirstLoadAndCleanupIsNotLost() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);
    var now = DateTimeOffset.UtcNow;
    await AddExpiredEntriesAsync(repository, 200, now).ConfigureAwait(false);

    var gate = new HookGate();
    repository.RemoveStepHookForTests = gate.HookAsync;

    var cleanup = repository.DeleteExpiredAsync(DateTimeOffset.UtcNow);
    var firstAdd = repository.AddAsync(CreateEntry("start-root", "queue", "q-3", now, "running"));
    await gate.Entered.ConfigureAwait(false);
    var secondAdd = repository.AddAsync(CreateEntry("start-root-2", "queue", "q-3", now.AddSeconds(1), "running"));
    await Task.Delay(WriteWait).ConfigureAwait(false);
    gate.Release();
    await cleanup.ConfigureAwait(false);
    await Task.WhenAll(firstAdd, secondAdd).ConfigureAwait(false);

    var roots = await repository.QueryAsync(new ExecutionLogQuery { ObjectId = "q-3", RootsOnly = true }).ConfigureAwait(false);
    roots.Items.Select(i => i.Id).Should().BeEquivalentTo(new[] { "start-root", "start-root-2" });
  }

  [Fact]
  public async Task ConcurrentWritesDuringCleanupLoop() {
    for (var run = 0; run < 20; run++) {
      var storageRoot = CreateTempStorageRoot();
      using var repository = new FileExecutionLogRepository(storageRoot);
      var now = DateTimeOffset.UtcNow;
      await AddExpiredEntriesAsync(repository, 200, now).ConfigureAwait(false);
      await repository.QueryAsync(new ExecutionLogQuery()).ConfigureAwait(false);

      var rootId = $"loop-root-{run}";
      var tasks = new List<Task> {
        repository.DeleteExpiredAsync(DateTimeOffset.UtcNow),
        repository.AddAsync(CreateEntry(rootId, "queue", "q-loop", now, "running"))
      };
      for (var i = 1; i <= 10; i++) {
        tasks.Add(repository.AddAsync(CreateChild($"{rootId}-child-{i}", rootId, now.AddSeconds(i), i)));
      }
      await Task.WhenAll(tasks).ConfigureAwait(false);

      var roots = await repository.QueryAsync(new ExecutionLogQuery { ObjectId = "q-loop", RootsOnly = true }).ConfigureAwait(false);
      roots.Items.Select(i => i.Id).Should().Contain(rootId, "run {0} must keep the root", run);
      var subtree = await repository.GetSubtreeAsync(rootId).ConfigureAwait(false);
      subtree.Should().HaveCount(11, "run {0} must keep the root and its 10 children", run);
    }
  }

  private static async Task<List<string>> AddExpiredEntriesAsync(FileExecutionLogRepository repository, int count, DateTimeOffset now) {
    var ids = new List<string>(count);
    for (var i = 0; i < count; i++) {
      var id = $"expired-{i}";
      ids.Add(id);
      await repository.AddAsync(CreateEntry(id, "command", id, now.AddDays(-31), "success", now.AddDays(-1))).ConfigureAwait(false);
    }
    return ids;
  }

  private static ExecutionLogEntry CreateEntry(string id, string type, string objectId, DateTimeOffset timestampUtc, string status, DateTimeOffset? retentionExpiresUtc = null)
    => new() {
      Id = id,
      TimestampUtc = timestampUtc,
      ExecutionType = type,
      FinalStatus = status,
      ObjectRef = new ExecutionObjectReference(type, objectId, objectId),
      Navigation = new ExecutionNavigationContext($"/authoring/{type}/{objectId}", null),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
      Summary = "Summary",
      RetentionExpiresUtc = retentionExpiresUtc ?? timestampUtc.AddDays(30)
    };

  private static ExecutionLogEntry CreateChild(string id, string rootId, DateTimeOffset timestampUtc, int sequenceIndex)
    => new() {
      Id = id,
      TimestampUtc = timestampUtc,
      ExecutionType = "sequence",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("sequence", "s-1", "Sequence"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/s-1", null),
      Hierarchy = new ExecutionHierarchyContext(rootId, rootId, 1, sequenceIndex),
      Summary = "Summary",
      RetentionExpiresUtc = timestampUtc.AddDays(30)
    };

  private static string CreateTempStorageRoot() {
    var root = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }

  /// <summary>Stops the remove step until the test releases it.</summary>
  private sealed class HookGate {
    private readonly TaskCompletionSource _entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Entered => _entered.Task;

    public Task HookAsync() {
      _entered.TrySetResult();
      return _release.Task;
    }

    public void Release() => _release.TrySetResult();
  }
}
