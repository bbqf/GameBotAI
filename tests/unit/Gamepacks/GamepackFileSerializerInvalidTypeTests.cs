using System.Linq;
using FluentAssertions;
using GameBot.Domain.Gamepacks;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 154: a bad type member sets InvalidType with the path and the reason, and never throws.</summary>
public sealed class GamepackFileSerializerInvalidTypeTests {
  [Theory]
  [InlineData("missing", "the type member is missing")]
  [InlineData("unknown", "the type value is unknown")]
  [InlineData("number", "the type value must be a string")]
  [InlineData("duplicate", "the type member is duplicated")]
  public void ParseSetsInvalidTypeWithThePathAndTheReason(string kind, string reasonText) {
    var condition = kind switch {
      "missing" => M.MissingType(),
      "unknown" => M.UnknownType(),
      "number" => M.NumberType(),
      _ => M.DuplicateType()
    };

    var result = GamepackFileSerializer.Parse(M.PackWithCondition(condition));

    result.InvalidType.Should().BeTrue();
    result.InvalidJson.Should().BeFalse();
    var problem = result.Problems.Single();
    problem.Category.Should().Be(GamepackProblemCategories.Structure);
    problem.Message.Should().Contain("$.sequences[0].steps[1].condition").And.Contain(reasonText);
  }

  [Fact]
  public void ANestedBadTypeGivesTheNestedPath() {
    var result = GamepackFileSerializer.Parse(M.PackWithNestedBreak(M.NestedBadItem));

    result.InvalidType.Should().BeTrue();
    result.Problems.Single().Message.Should().Contain("$.sequences[0].steps[0].breakCondition.children[1]");
  }

  [Fact]
  public void AValidFileDoesNotSetInvalidType() {
    GamepackFileSerializer.Parse(M.PackWithCondition(M.ConditionLast)).InvalidType.Should().BeFalse();
  }
}
