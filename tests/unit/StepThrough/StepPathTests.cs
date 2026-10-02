using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Services.StepThrough;
using Xunit;
using static GameBot.UnitTests.StepThrough.StepperTestKit;

#pragma warning disable CA2007, CA1861, CA1859, CA1849

namespace GameBot.UnitTests.StepThrough;

public sealed class StepPathTests {
  [Fact]
  public void FlattenListsTopLevelStepsInRunOrder() {
    var sequence = Sequence(Cmd(2, "c"), Cmd(0, "a"), Cmd(1, "b"));

    var nodes = StepPath.Flatten(sequence.Steps);

    nodes.Select(n => n.Path).Should().Equal("0", "1", "2");
    nodes.Select(n => n.StepId).Should().Equal("a", "b", "c");
    nodes.Should().OnlyContain(n => n.Depth == 0 && n.Selectable && !n.Container);
  }

  [Fact]
  public void FlattenIndentsLoopBodyAndMarksHeaderRowAsNotSelectable() {
    var sequence = Sequence(
      CountLoop(0, "loop", 2, Cmd(0, "in1"), Cmd(1, "in2")),
      Cmd(1, "after"));

    var nodes = StepPath.Flatten(sequence.Steps);

    nodes.Select(n => n.Path).Should().Equal("0", "0/body/0", "0/body/1", "1");
    nodes.Select(n => n.Depth).Should().Equal(0, 1, 1, 0);
    nodes[0].Container.Should().BeTrue();
    nodes[0].Selectable.Should().BeFalse();
    nodes[1].Selectable.Should().BeTrue();
    nodes[1].Branch.Should().Be("body");
  }

  [Fact]
  public void FlattenListsThenAndElseBranchesOfAnIfStep() {
    var sequence = Sequence(
      If(0, "if", new[] { Cmd(0, "then1") }, new[] { Cmd(0, "else1"), Cmd(1, "else2") }));

    var nodes = StepPath.Flatten(sequence.Steps);

    nodes.Select(n => n.Path).Should().Equal("0", "0/body/0", "0/else/0", "0/else/1");
    nodes.Select(n => n.Branch).Should().Equal(null, "body", "else", "else");
    nodes[2].Depth.Should().Be(1);
  }

  [Fact]
  public void FlattenGivesAPathToAStepWithAnEmptyStepId() {
    var step = Cmd(0, "x");
    step.StepId = string.Empty;
    var sequence = Sequence(step);

    var nodes = StepPath.Flatten(sequence.Steps);

    nodes.Should().ContainSingle();
    nodes[0].StepId.Should().BeNull();
    nodes[0].Path.Should().Be("0");
  }

  [Fact]
  public void FlattenSupportsNestedLoopBodyInsideIfBranch() {
    var sequence = Sequence(
      If(0, "if", new[] { CountLoop(0, "inner", 1, Cmd(0, "deep")) }));

    var nodes = StepPath.Flatten(sequence.Steps);

    nodes.Select(n => n.Path).Should().Equal("0", "0/body/0", "0/body/0/body/0");
    nodes.Select(n => n.Depth).Should().Equal(0, 1, 2);
  }

  [Theory]
  [InlineData("0", "a")]
  [InlineData("1/body/0", "in1")]
  [InlineData("1/body/1", "in2")]
  [InlineData("2/else/0", "e1")]
  public void TryResolveFindsTheStepOfAValidPath(string path, string expectedStepId) {
    var sequence = Sequence(
      Cmd(0, "a"),
      CountLoop(1, "loop", 2, Cmd(0, "in1"), Cmd(1, "in2")),
      If(2, "if", new[] { Cmd(0, "t1") }, new[] { Cmd(0, "e1") }));

    StepPath.TryResolve(sequence.Steps, path, out var resolved).Should().BeTrue();

    resolved!.Step.StepId.Should().Be(expectedStepId);
    resolved.Path.Should().Be(path);
  }

  [Theory]
  [InlineData("")]
  [InlineData("9")]
  [InlineData("-1")]
  [InlineData("a")]
  [InlineData("0/body/0")]
  [InlineData("1/else/0")]
  [InlineData("1/body")]
  [InlineData("1/body/9")]
  [InlineData("1/nope/0")]
  public void TryResolveRejectsAPathThatIsNotInTheTree(string path) {
    var sequence = Sequence(
      Cmd(0, "a"),
      CountLoop(1, "loop", 2, Cmd(0, "in1")));

    StepPath.TryResolve(sequence.Steps, path, out _).Should().BeFalse();
  }

  [Fact]
  public void SiblingBuildsThePathOfANeighbourInTheSameList() {
    var sequence = Sequence(CountLoop(0, "loop", 2, Cmd(0, "in1"), Cmd(1, "in2")), Cmd(1, "after"));
    StepPath.TryResolve(sequence.Steps, "0/body/0", out var resolved).Should().BeTrue();

    StepPath.Sibling(resolved!, 1).Should().Be("0/body/1");
  }

  [Fact]
  public void ACommandStepShowsTheNameOfTheCommandAndFallsBackToTheId() {
    var named = new SequenceStep {
      Order = 0,
      StepId = "n",
      CommandId = "cmd-1",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "command" },
      CommandReference = new SequenceCommandReference { CommandId = "cmd-1", CommandName = "Open menu" }
    };
    var unnamed = new SequenceStep {
      Order = 1,
      StepId = "u",
      CommandId = "cmd-2",
      StepType = SequenceStepType.Action,
      Action = new SequenceActionPayload { Type = "command" }
    };

    StepPath.Label(named).Should().Be("command Open menu");
    StepPath.Label(unnamed).Should().Be("command cmd-2");
    StepPath.TypeName(named).Should().Be("command");
    StepPath.TypeName(unnamed).Should().Be("command");
  }

  [Fact]
  public void LabelUsesTheStepLabelOrAShortDescription() {
    var labeled = Cmd(0, "a");
    labeled.Label = "Open the menu";
    var tap = Cmd(1, "b");
    tap.Action!.Parameters["x"] = 100;
    tap.Action.Parameters["y"] = 200;

    StepPath.Label(labeled).Should().Be("Open the menu");
    StepPath.Label(tap).Should().Be("tap 100,200");
    StepPath.Label(CountLoop(2, "l", 3)).Should().Be("loop 3 times");
  }
}
