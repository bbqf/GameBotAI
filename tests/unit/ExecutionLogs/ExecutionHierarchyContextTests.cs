using FluentAssertions;
using GameBot.Service.Services.ExecutionLog;
using Xunit;

namespace GameBot.UnitTests.ExecutionLogs;

public sealed class ExecutionHierarchyContextTests {
  [Fact]
  public void BuildUsesParentAsRootWhenRootNotProvided() {
    var context = ExecutionHierarchyBuilder.Build(new ExecutionLogContext {
      ParentExecutionId = "parent-001",
      Depth = 2,
      SequenceIndex = 3
    });

    context.RootExecutionId.Should().Be("parent-001");
    context.ParentExecutionId.Should().Be("parent-001");
    context.Depth.Should().Be(2);
    context.SequenceIndex.Should().Be(3);
  }

  [Fact]
  public void BuildPrefersExplicitRootExecutionIdWhenProvided() {
    var context = ExecutionHierarchyBuilder.Build(new ExecutionLogContext {
      RootExecutionId = "root-123",
      ParentExecutionId = "parent-002",
      Depth = -5
    });

    context.RootExecutionId.Should().Be("root-123");
    context.ParentExecutionId.Should().Be("parent-002");
    context.Depth.Should().Be(0);
  }

  [Fact]
  public void BuildGeneratesRootWhenNoContextProvided() {
    var context = ExecutionHierarchyBuilder.Build(null);

    context.RootExecutionId.Should().NotBeNullOrWhiteSpace();
    context.ParentExecutionId.Should().BeNull();
    context.Depth.Should().Be(0);
  }

  [Fact]
  public void NavigationBuilderCreatesDirectAndParentPathsForNestedCommand() {
    var context = ExecutionNavigationBuilder.Build("command", "cmd-001", new ExecutionLogContext {
      ParentObjectType = "sequence",
      ParentObjectId = "seq-001"
    });

    context.DirectPath.Should().Be("/authoring/commands/cmd-001");
    context.ParentPath.Should().Be("/authoring/sequences/seq-001");
    context.PathKind.Should().Be("relative-route");
  }
}
