using FluentAssertions;
using GameBot.Domain.Logging;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ExecutionLogEntryMappingTests {
  [Fact]
  public async Task AddAndGetRoundTripPreservesRequiredExecutionLogFields() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    var entry = new ExecutionLogEntry {
      Id = "entry-mapping-001",
      TimestampUtc = now,
      ExecutionType = "command",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("command", "cmd-001", "Command Snapshot", "v1"),
      Navigation = new ExecutionNavigationContext("/authoring/commands/cmd-001", "/authoring/sequences/seq-001"),
      Hierarchy = new ExecutionHierarchyContext("root-001", "parent-001", 1, 2),
      Summary = "Command executed successfully",
      Details = new[] {
        new ExecutionDetailItem("tap", "Tap executed", new Dictionary<string, object?> { ["x"] = 15, ["y"] = 25 }, "normal")
      },
      StepOutcomes = new[] {
        new ExecutionStepOutcome(1, "primitiveTap", "executed", null, null)
      },
      RetentionExpiresUtc = now.AddDays(30)
    };

    await repository.AddAsync(entry).ConfigureAwait(false);
    var saved = await repository.GetAsync(entry.Id).ConfigureAwait(false);

    saved.Should().NotBeNull();
    saved!.Id.Should().Be(entry.Id);
    saved.ExecutionType.Should().Be("command");
    saved.FinalStatus.Should().Be("success");
    saved.ObjectRef.ObjectType.Should().Be("command");
    saved.ObjectRef.ObjectId.Should().Be("cmd-001");
    saved.ObjectRef.DisplayNameSnapshot.Should().Be("Command Snapshot");
    saved.Navigation.DirectPath.Should().Be("/authoring/commands/cmd-001");
    saved.Hierarchy.RootExecutionId.Should().Be("root-001");
    saved.StepOutcomes.Should().ContainSingle();
    saved.Details.Should().ContainSingle();
  }

  [Fact]
  public async Task QueryFiltersOnRequiredIdentityAndStatusFields() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    await repository.AddAsync(new ExecutionLogEntry {
      Id = "entry-filter-hit",
      TimestampUtc = DateTimeOffset.UtcNow,
      ExecutionType = "sequence",
      FinalStatus = "failure",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-keep", "Keep Sequence"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-keep", null),
      Hierarchy = new ExecutionHierarchyContext("entry-filter-hit", null, 0, null),
      Summary = "Failed",
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
    }).ConfigureAwait(false);

    await repository.AddAsync(new ExecutionLogEntry {
      Id = "entry-filter-miss",
      TimestampUtc = DateTimeOffset.UtcNow,
      ExecutionType = "sequence",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("sequence", "seq-skip", "Skip Sequence"),
      Navigation = new ExecutionNavigationContext("/authoring/sequences/seq-skip", null),
      Hierarchy = new ExecutionHierarchyContext("entry-filter-miss", null, 0, null),
      Summary = "Succeeded",
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
    }).ConfigureAwait(false);

    var page = await repository.QueryAsync(new ExecutionLogQuery {
      ObjectType = "sequence",
      ObjectId = "seq-keep",
      FinalStatus = "failure",
      PageSize = 10
    }).ConfigureAwait(false);

    page.Items.Should().ContainSingle();
    page.Items[0].Id.Should().Be("entry-filter-hit");
    page.Items[0].ObjectRef.DisplayNameSnapshot.Should().Be("Keep Sequence");
  }

  private static string CreateTempStorageRoot() {
    var root = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }
}
