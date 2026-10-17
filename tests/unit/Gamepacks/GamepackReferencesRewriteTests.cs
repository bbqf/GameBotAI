using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;
using F = GameBot.UnitTests.Gamepacks.GamepackStepFixtures;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 151: the command id rewrite changes the payload copy too.</summary>
public sealed class GamepackReferencesRewriteTests {
  private static CommandSequence Read(params string[] steps) => GamepackFileSerializer.Parse(F.PackWith(steps)).File!.Sequences.Single();

  private static void ExpectId(SequenceStep step, string id) {
    step.CommandId.Should().Be(id);
    step.CommandReference!.CommandId.Should().Be(id);
    step.Action!.Parameters["commandId"]!.ToString().Should().Be(id);
  }

  [Fact]
  public void TheRewriteChangesAllThreeCopiesInTheRootList() {
    var sequence = Read(F.All());
    GamepackReferences.RewriteCommandIds(sequence, id => id == F.CommandId ? "new-id" : null);
    ExpectId(sequence.Steps.Single(), "new-id");
  }

  [Fact]
  public void TheRewriteChangesAllThreeCopiesInANestedStep() {
    var sequence = Read(F.InLoop(F.All("a")), F.InConditionalBody(F.PrimitiveOnly("b")), F.InConditionalElseBody(F.ReferenceOnly("c")));
    GamepackReferences.RewriteCommandIds(sequence, id => id == F.CommandId ? "new-id" : null);
    ExpectId(sequence.Steps[0].Body.Single(), "new-id");
    ExpectId(sequence.Steps[1].Body.Single(), "new-id");
    ExpectId(sequence.Steps[2].ElseBody!.Single(), "new-id");
  }

  [Fact]
  public void TheIdStaysWhenTheMapKeepsIt() {
    var sequence = Read(F.All());
    GamepackReferences.RewriteCommandIds(sequence, _ => null);
    ExpectId(sequence.Steps.Single(), F.CommandId);
  }
}
