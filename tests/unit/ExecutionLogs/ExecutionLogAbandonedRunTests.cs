using FluentAssertions;
using GameBot.Domain.Logging;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 198 (B-044, FR-006, FR-007): the close rule of a "running" entry of an earlier service process.
/// </summary>
public sealed class ExecutionLogAbandonedRunTests {
  [Fact]
  public void CloseOfQueueEntryGivesFailureAndQueueSummary() {
    var closed = ExecutionLogAbandonedRun.Close(CreateEntry("queue", "PNS.Production (Exo)"));

    closed.FinalStatus.Should().Be("failure");
    closed.Summary.Should().Be("Queue 'PNS.Production (Exo)' run ended when the service stopped.");
  }

  [Fact]
  public void CloseOfSequenceEntryGivesSequenceSummary() {
    var closed = ExecutionLogAbandonedRun.Close(CreateEntry("sequence", "Farm"));

    closed.FinalStatus.Should().Be("failure");
    closed.Summary.Should().Be("Sequence 'Farm' run ended when the service stopped.");
  }

  [Fact]
  public void CloseOfCommandEntryGivesNameOnlySummary() {
    var closed = ExecutionLogAbandonedRun.Close(CreateEntry("command", "Tap"));

    closed.Summary.Should().Be("'Tap' run ended when the service stopped.");
  }

  [Fact]
  public void CloseUsesObjectIdWhenNameIsEmpty() {
    var closed = ExecutionLogAbandonedRun.Close(CreateEntry("queue", " ", objectId: "q-empty"));

    closed.Summary.Should().Be("Queue 'q-empty' run ended when the service stopped.");
  }

  [Fact]
  public void CloseTrimsLongSummaryTo240Characters() {
    var closed = ExecutionLogAbandonedRun.Close(CreateEntry("queue", new string('x', 300)));

    closed.Summary.Should().HaveLength(240);
  }

  [Fact]
  public void CloseKeepsAllOtherFields() {
    var entry = new ExecutionLogEntry {
      Id = "entry-1",
      TimestampUtc = new DateTimeOffset(2026, 11, 19, 7, 7, 17, TimeSpan.Zero),
      ExecutionType = "queue",
      FinalStatus = "running",
      ObjectRef = new ExecutionObjectReference("queue", "q-1", "Exo", "v1"),
      Navigation = new ExecutionNavigationContext("/authoring/queues/q-1", "/authoring/queues"),
      Hierarchy = new ExecutionHierarchyContext("root-1", "parent-1", 1, 3),
      Summary = "Queue 'Exo' running.",
      Details = new[] { new ExecutionDetailItem("note", "detail", null, "normal") },
      StepOutcomes = new[] { new ExecutionStepOutcome(1, "command", "executed", null, null) },
      RetentionExpiresUtc = new DateTimeOffset(2026, 11, 26, 0, 0, 0, TimeSpan.Zero),
      RotatedToExecutionId = "next-root",
      RotatedFromExecutionId = "prev-root",
      CancellationReason = ExecutionCancellationReasons.SequenceTimeLimit,
      TimeLimitMs = 1000,
      Origin = ExecutionOrigins.StepThrough
    };

    var closed = ExecutionLogAbandonedRun.Close(entry);

    closed.Should().BeEquivalentTo(entry, o => o.Excluding(e => e.FinalStatus).Excluding(e => e.Summary));
    closed.Details.Should().BeSameAs(entry.Details);
    closed.StepOutcomes.Should().BeSameAs(entry.StepOutcomes);
  }

  [Fact]
  public void CloseOfFinishedEntryGivesSameEntry() {
    var entry = CreateEntry("queue", "Exo", status: "success");

    ExecutionLogAbandonedRun.Close(entry).Should().BeSameAs(entry);
  }

  [Fact]
  public async Task RepositoryClosesRunningEntriesBeforeProcessStart() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);
    var processStart = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateEntry("queue", "Exo", id: "old-root", timestamp: processStart.AddHours(-8))).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("sequence", "Farm", id: "old-child", rootId: "old-root", timestamp: processStart.AddHours(-7))).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("queue", "Exo", id: "new-root", timestamp: processStart)).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("queue", "Exo", id: "done-root", status: "success", timestamp: processStart.AddHours(-9))).ConfigureAwait(false);

    var closed = await repository.CloseAbandonedRunningAsync(processStart).ConfigureAwait(false);

    closed.Should().Be(2);
    (await repository.GetAsync("old-root").ConfigureAwait(false))!.FinalStatus.Should().Be("failure");
    (await repository.GetAsync("old-child").ConfigureAwait(false))!.FinalStatus.Should().Be("failure");
    (await repository.GetAsync("new-root").ConfigureAwait(false))!.FinalStatus.Should().Be("running");
    var done = await repository.GetAsync("done-root").ConfigureAwait(false);
    done!.FinalStatus.Should().Be("success");
    done.Summary.Should().Be("Summary");

    var page = await repository.QueryAsync(new ExecutionLogQuery { RootsOnly = true }).ConfigureAwait(false);
    page.Items.Single(i => i.Id == "old-root").FinalStatus.Should().Be("failure");
    page.Items.Single(i => i.Id == "new-root").FinalStatus.Should().Be("running");
    var subtree = await repository.GetSubtreeAsync("old-root").ConfigureAwait(false);
    subtree.Should().HaveCount(2).And.OnlyContain(e => e.FinalStatus == "failure");

    var second = await repository.CloseAbandonedRunningAsync(processStart).ConfigureAwait(false);
    second.Should().Be(0);
  }

  private static ExecutionLogEntry CreateEntry(
    string type,
    string name,
    string? objectId = null,
    string status = "running",
    string id = "entry",
    string? rootId = null,
    DateTimeOffset? timestamp = null) {
    var ts = timestamp ?? DateTimeOffset.UtcNow.AddHours(-1);
    return new ExecutionLogEntry {
      Id = id,
      TimestampUtc = ts,
      ExecutionType = type,
      FinalStatus = status,
      ObjectRef = new ExecutionObjectReference(type, objectId ?? $"{type}-1", name),
      Navigation = new ExecutionNavigationContext($"/authoring/{type}/1", null),
      Hierarchy = rootId is null
        ? new ExecutionHierarchyContext(id, null, 0, null)
        : new ExecutionHierarchyContext(rootId, rootId, 1, 1),
      Summary = "Summary",
      RetentionExpiresUtc = ts.AddDays(30)
    };
  }

  private static string CreateTempStorageRoot() {
    var root = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }
}
