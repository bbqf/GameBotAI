using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class HistoricalLogBackwardCompatIntegrationTests {
  [Fact]
  public async Task HistoricalEntriesRenderAsCompletedLeafRootsWithoutError() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();
    var repository = app.Services.GetRequiredService<IExecutionLogRepository>();
    var svc = app.Services.GetRequiredService<IExecutionLogService>();

    // Simulate a pre-feature entry: no "running" status, no linked children, root hierarchy.
    var legacy = new ExecutionLogEntry {
      Id = "legacy-1",
      TimestampUtc = DateTimeOffset.UtcNow.AddDays(-1),
      ExecutionType = "command",
      FinalStatus = "success",
      ObjectRef = new ExecutionObjectReference("command", "cmd-legacy", "Legacy Command"),
      Navigation = new ExecutionNavigationContext("/authoring/commands/cmd-legacy", null),
      Hierarchy = new ExecutionHierarchyContext("legacy-1", null, 0, null),
      Summary = "Legacy command executed.",
      RetentionExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
    };
    await repository.AddAsync(legacy).ConfigureAwait(false);

    // Appears in the roots-only list (it is top-level).
    var roots = await svc.QueryAsync(new ExecutionLogQuery { RootsOnly = true, PageSize = 50 }).ConfigureAwait(false);
    roots.Items.Should().Contain(i => i.Id == "legacy-1");

    // Detail and subtree both resolve without error; the subtree is a leaf root.
    var detail = await svc.GetAsync("legacy-1").ConfigureAwait(false);
    detail.Should().NotBeNull();

    var subtree = await svc.GetSubtreeAsync("legacy-1").ConfigureAwait(false);
    subtree.Should().NotBeNull();
    subtree!.Root.ExecutionId.Should().Be("legacy-1");
    subtree.Root.Children.Should().BeEmpty("a historical command has no recorded sub-elements");
  }
}
