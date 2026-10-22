using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 161 (T008): the rules of the failure explainer.</summary>
public sealed class GamepackFailureExplainerTests {
  private static GamepackProblem UserConflict(string name = "A") =>
    new(GamepackProblemCategories.Conflict, GamepackObjectTypes.Sequence, name, "Conflict.", Owner: "user");

  private static GamepackProblem OtherConflict(string name = "B") =>
    new(GamepackProblemCategories.Conflict, GamepackObjectTypes.Command, name, "Conflict.", Owner: "alpha 1.0.0");

  private static GamepackProblem Reference() =>
    new(GamepackProblemCategories.Reference, GamepackObjectTypes.Sequence, "S", "Missing.");

  private static GamepackProblem Signature() =>
    new(GamepackProblemCategories.Signature, GamepackObjectTypes.Manifest, "P", "Rejected.", Code: "signature_untrusted");

  private static GamepackProblem Structure() =>
    new(GamepackProblemCategories.Structure, GamepackObjectTypes.Manifest, null, "Bad.");

  [Fact]
  public void SummaryCountsAndOrdersTheCategories() {
    var summary = GamepackFailureExplainer.Summarize(
      new[] { UserConflict("A"), Reference(), UserConflict("B"), Structure(), Signature() }, takeOverOn: false);

    summary.Entries.Select(e => e.Category).Should().Equal("structure", "signature", "reference", "conflict");
    summary.Entries.Select(e => e.Count).Should().Equal(1, 1, 1, 2);
    summary.Headline.Should().Be("1 structure problem, 1 signature problem, 1 reference problem, 2 conflicts.");
  }

  [Fact]
  public void LabelsUseTheSingularFormForCountOne() {
    var one = GamepackFailureExplainer.Summarize(new[] { UserConflict() }, false).Entries.Single();
    var two = GamepackFailureExplainer.Summarize(new[] { UserConflict("A"), UserConflict("B") }, false).Entries.Single();

    one.Label.Should().StartWith("conflict: ");
    two.Label.Should().StartWith("conflicts: ");
  }

  [Fact]
  public void AnEmptyListGivesTheNoProblemsHeadline() {
    var summary = GamepackFailureExplainer.Summarize(System.Array.Empty<GamepackProblem>(), false);

    summary.Headline.Should().Be("No problems.");
    summary.Entries.Should().BeEmpty();
  }

  [Fact]
  public void AUserConflictIsResolvedByTakeOver() {
    GamepackFailureExplainer.Explain(UserConflict(), false).TakeOverResolves.Should().BeTrue();
    GamepackFailureExplainer.Explain(UserConflict(), false).Remedy.Should().Contain("take-over");
  }

  [Fact]
  public void ASecondUserConflictWithTheSameNameIsResolvedToo() {
    var summary = GamepackFailureExplainer.Summarize(new[] { UserConflict("Same"), UserConflict("Same") }, false);

    summary.Entries.Single().Count.Should().Be(2);
    summary.Entries.Single().ResolvedByTakeOver.Should().BeTrue();
  }

  [Fact]
  public void OtherProblemsAreNotResolvedByTakeOver() {
    foreach (var problem in new[] { OtherConflict(), Reference(), Signature(), Structure() }) {
      var explanation = GamepackFailureExplainer.Explain(problem, false);
      explanation.TakeOverResolves.Should().BeFalse();
      explanation.Remedy.Should().NotBeNullOrWhiteSpace();
    }
  }

  [Fact]
  public void WithTakeOverOnNoProblemIsResolved() {
    GamepackFailureExplainer.Explain(UserConflict(), true).TakeOverResolves.Should().BeFalse();
    GamepackFailureExplainer.Summarize(new[] { UserConflict() }, true).Entries.Single().ResolvedByTakeOver.Should().BeFalse();
  }

  [Fact]
  public void TheUpdateInProgressConflictHasItsOwnRemedy() {
    var problem = new GamepackProblem(
      GamepackProblemCategories.Conflict, GamepackObjectTypes.Manifest, "P", "Busy.", Code: "update_in_progress");

    var explanation = GamepackFailureExplainer.Explain(problem, false);

    explanation.TakeOverResolves.Should().BeFalse();
    explanation.Remedy.Should().Contain("Try again");
  }

  [Fact]
  public void AnApiMajorVersionProblemUsesTheStructureRemedy() {
    var problem = new GamepackProblem(
      GamepackProblemCategories.Structure, GamepackObjectTypes.Manifest, "P", "The gamepack needs API major version 9.");

    GamepackFailureExplainer.Explain(problem, false).Remedy.Should().StartWith("Correct the gamepack file.");
  }

  [Fact]
  public void AMixedConflictEntryIsNotResolvedByTakeOver() {
    var entry = GamepackFailureExplainer.Summarize(new[] { UserConflict(), OtherConflict() }, false).Entries.Single();

    entry.ResolvedByTakeOver.Should().BeFalse();
    entry.Label.Should().Contain("owned by the user").And.Contain("owned by another gamepack");
  }
}
