using System.Text.Json;
using FluentAssertions;
using GameBot.Domain.Logging;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

/// <summary>
/// Feature 094 (FR-010): entries written before <c>cancellationReason</c>/<c>timeLimitMs</c> existed
/// must keep loading, and the new fields must survive the repository round-trip.
/// </summary>
public sealed class ExecutionLogEntryCompatTests {
  // The repository's own options (FileExecutionLogRepository): web defaults.
  private static readonly JsonSerializerOptions RepositoryOptions = new(JsonSerializerDefaults.Web);

  [Fact]
  public void LegacyEntryWithoutTheNewFieldsDeserializesWithNulls() {
    const string legacy = """
      {
        "id": "legacy-1",
        "timestampUtc": "2026-09-01T10:00:00+00:00",
        "executionType": "sequence",
        "finalStatus": "failure",
        "objectRef": { "objectType": "sequence", "objectId": "s1", "displayNameSnapshot": "S1" },
        "navigation": { "directPath": "/sequences/s1", "pathKind": "relative-route" },
        "hierarchy": { "rootExecutionId": "legacy-1", "depth": 0 },
        "summary": "Sequence 'S1' was cancelled before it finished."
      }
      """;

    var entry = JsonSerializer.Deserialize<ExecutionLogEntry>(legacy, RepositoryOptions);

    entry.Should().NotBeNull();
    entry!.FinalStatus.Should().Be("failure");
    entry.CancellationReason.Should().BeNull();
    entry.TimeLimitMs.Should().BeNull();
  }

  [Fact]
  public async Task RepositoryRoundTripPreservesTheCancellationFields() {
    var root = Path.Combine(Path.GetTempPath(), "gamebot-094-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try {
      using var repository = new FileExecutionLogRepository(root);
      var entry = new ExecutionLogEntry {
        Id = "timed-out-1",
        ExecutionType = "sequence",
        FinalStatus = "failure",
        ObjectRef = new ExecutionObjectReference("sequence", "s1", "S1"),
        Navigation = new ExecutionNavigationContext("/sequences/s1", null),
        Hierarchy = new ExecutionHierarchyContext("timed-out-1", null, 0, null),
        RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
        CancellationReason = ExecutionCancellationReasons.SequenceTimeLimit,
        TimeLimitMs = 240000
      };

      await repository.AddAsync(entry).ConfigureAwait(false);
      var loaded = await repository.GetAsync("timed-out-1").ConfigureAwait(false);

      loaded!.CancellationReason.Should().Be("sequence_time_limit");
      loaded.TimeLimitMs.Should().Be(240000);
    }
    finally {
      Directory.Delete(root, recursive: true);
    }
  }
}
