using FluentAssertions;
using GameBot.Domain.Logging;
using GameBot.Service.Services;
using GameBot.Service.Services.ExecutionLog;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace GameBot.IntegrationTests.ExecutionLogs;

[Collection("ConfigIsolation")]
public sealed class ExecutionHierarchyIntegrationTests {
  [Fact]
  public async Task NestedSequenceAndCommandPersistParentChildHierarchy() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();

    var logService = app.Services.GetRequiredService<IExecutionLogService>();

    await logService.LogSequenceExecutionAsync(
      "seq-parent",
      "Parent Sequence",
      "success",
      "Parent executed",
      new ExecutionLogContext { Depth = 0 },
      details: null).ConfigureAwait(false);

    var parent = (await logService.QueryAsync(new ExecutionLogQuery {
      ObjectType = "sequence",
      ObjectId = "seq-parent",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    await logService.LogCommandExecutionAsync(
      "cmd-child",
      "Child Command",
      "success",
      Array.Empty<PrimitiveTapStepOutcome>(),
      new ExecutionLogContext {
        ParentExecutionId = parent.Id,
        RootExecutionId = parent.Hierarchy.RootExecutionId,
        ParentObjectType = "sequence",
        ParentObjectId = "seq-parent",
        Depth = 1,
        SequenceIndex = 0
      }).ConfigureAwait(false);

    var child = (await logService.QueryAsync(new ExecutionLogQuery {
      ObjectType = "command",
      ObjectId = "cmd-child",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    child.Hierarchy.ParentExecutionId.Should().Be(parent.Id);
    child.Hierarchy.RootExecutionId.Should().Be(parent.Hierarchy.RootExecutionId);
    child.Hierarchy.Depth.Should().Be(1);
    child.Hierarchy.SequenceIndex.Should().Be(0);
  }

  [Fact]
  public async Task NestedCommandPersistsDirectAndParentNavigationPaths() {
    TestEnvironment.PrepareCleanDataDir();

    using var app = new WebApplicationFactory<Program>();
    _ = app.CreateClient();

    var logService = app.Services.GetRequiredService<IExecutionLogService>();

    await logService.LogSequenceExecutionAsync(
      "seq-navigate",
      "Navigation Parent",
      "success",
      "Parent executed",
      new ExecutionLogContext { Depth = 0 },
      details: null).ConfigureAwait(false);

    var parent = (await logService.QueryAsync(new ExecutionLogQuery {
      ObjectType = "sequence",
      ObjectId = "seq-navigate",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    await logService.LogCommandExecutionAsync(
      "cmd-navigate",
      "Navigation Child",
      "failure",
      Array.Empty<PrimitiveTapStepOutcome>(),
      new ExecutionLogContext {
        ParentExecutionId = parent.Id,
        RootExecutionId = parent.Hierarchy.RootExecutionId,
        ParentObjectType = "sequence",
        ParentObjectId = "seq-navigate",
        Depth = 1
      }).ConfigureAwait(false);

    var child = (await logService.QueryAsync(new ExecutionLogQuery {
      ObjectType = "command",
      ObjectId = "cmd-navigate",
      PageSize = 1
    }).ConfigureAwait(false)).Items.Single();

    child.Navigation.DirectPath.Should().Be("/authoring/commands/cmd-navigate");
    child.Navigation.ParentPath.Should().Be("/authoring/sequences/seq-navigate");
    child.Navigation.PathKind.Should().Be("relative-route");
  }
}
