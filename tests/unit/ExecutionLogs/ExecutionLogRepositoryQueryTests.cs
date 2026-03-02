using FluentAssertions;
using GameBot.Domain.Logging;
using System.Globalization;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ExecutionLogRepositoryQueryTests {
  [Fact]
  public async Task QueryAppliesCaseInsensitiveContainsFiltersAcrossColumns() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateEntry("entry-a", now.AddMinutes(-2), "Farm Run", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("entry-b", now.AddMinutes(-1), "Boss Run", "failure")).ConfigureAwait(false);

    var page = await repository.QueryAsync(new ExecutionLogQuery {
      FilterObjectName = "farm",
      FilterStatus = "SUC",
      FilterTimestamp = now.AddMinutes(-2).ToLocalTime().Year.ToString(CultureInfo.InvariantCulture),
      PageSize = 50
    }).ConfigureAwait(false);

    page.Items.Should().ContainSingle();
    page.Items[0].Id.Should().Be("entry-a");
  }

  [Theory]
  [InlineData("timestamp", "desc", "entry-c")]
  [InlineData("timestamp", "asc", "entry-a")]
  [InlineData("objectName", "asc", "entry-b")]
  [InlineData("status", "asc", "entry-b")]
  public async Task QuerySortsByRequestedColumn(string sortBy, string sortDirection, string expectedFirstId) {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateEntry("entry-a", now.AddMinutes(-3), "Zulu", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("entry-b", now.AddMinutes(-2), "Alpha", "failure")).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("entry-c", now.AddMinutes(-1), "Echo", "success")).ConfigureAwait(false);

    var page = await repository.QueryAsync(new ExecutionLogQuery {
      SortBy = sortBy,
      SortDirection = sortDirection,
      PageSize = 50
    }).ConfigureAwait(false);

    page.Items.Should().NotBeEmpty();
    page.Items[0].Id.Should().Be(expectedFirstId);
  }

  [Fact]
  public async Task QueryUsesPageTokenForPagination() {
    var storageRoot = CreateTempStorageRoot();
    using var repository = new FileExecutionLogRepository(storageRoot);

    var now = DateTimeOffset.UtcNow;
    await repository.AddAsync(CreateEntry("entry-a", now.AddMinutes(-3), "One", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("entry-b", now.AddMinutes(-2), "Two", "success")).ConfigureAwait(false);
    await repository.AddAsync(CreateEntry("entry-c", now.AddMinutes(-1), "Three", "success")).ConfigureAwait(false);

    var firstPage = await repository.QueryAsync(new ExecutionLogQuery { PageSize = 2 }).ConfigureAwait(false);
    firstPage.Items.Should().HaveCount(2);
    firstPage.NextCursor.Should().NotBeNullOrWhiteSpace();

    var secondPage = await repository.QueryAsync(new ExecutionLogQuery {
      PageSize = 2,
      PageToken = firstPage.NextCursor
    }).ConfigureAwait(false);

    secondPage.Items.Should().ContainSingle();
    secondPage.Items[0].Id.Should().Be("entry-a");
  }

  private static ExecutionLogEntry CreateEntry(string id, DateTimeOffset timestampUtc, string objectName, string status)
    => new() {
      Id = id,
      TimestampUtc = timestampUtc,
      ExecutionType = "command",
      FinalStatus = status,
      ObjectRef = new ExecutionObjectReference("command", id, objectName),
      Navigation = new ExecutionNavigationContext($"/authoring/commands/{id}", null),
      Hierarchy = new ExecutionHierarchyContext(id, null, 0, null),
      Summary = "Summary",
      RetentionExpiresUtc = timestampUtc.AddDays(30)
    };

  private static string CreateTempStorageRoot() {
    var root = Path.Combine(Path.GetTempPath(), "GameBot.UnitTests", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    return root;
  }
}
