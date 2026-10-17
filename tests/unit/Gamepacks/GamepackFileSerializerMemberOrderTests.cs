using System.Linq;
using FluentAssertions;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;
using Xunit;
using M = GameBot.UnitTests.Gamepacks.GamepackMemberOrderFixtures;

#pragma warning disable CA1707, CA2007, CA1861

namespace GameBot.UnitTests.Gamepacks;

/// <summary>Feature 154: the type member can be at any position in a condition or a loop config.</summary>
public sealed class GamepackFileSerializerMemberOrderTests {
  private static SequenceStepCondition? ConditionOf(string conditionJson) {
    var result = GamepackFileSerializer.Parse(M.PackWithCondition(conditionJson));
    result.Problems.Should().BeEmpty();
    result.InvalidType.Should().BeFalse();
    return result.File!.Sequences.Single().Steps.Single(s => s.StepId == "c1").Condition;
  }

  [Fact]
  public void TheTypeMemberFirstMiddleAndLastGiveEqualConditions() {
    var first = ConditionOf(M.ConditionFirst);
    var middle = ConditionOf(M.ConditionMiddle);
    var last = ConditionOf(M.ConditionLast);

    first.Should().BeOfType<CommandOutcomeStepCondition>();
    middle.Should().BeEquivalentTo(first, o => o.RespectingRuntimeTypes());
    last.Should().BeEquivalentTo(first, o => o.RespectingRuntimeTypes());
    ((CommandOutcomeStepCondition)last!).StepRef.Should().Be("s0");
  }

  [Fact]
  public void ANestedConditionWithTheTypeMemberLastParses() {
    var json = "{\"type\":\"all\",\"children\":[{\"expectedState\":\"success\",\"stepRef\":\"s0\",\"type\":\"commandOutcome\"}]}";

    var condition = ConditionOf(json);

    var all = condition.Should().BeOfType<AllStepCondition>().Subject;
    all.Children.Single().Should().BeOfType<CommandOutcomeStepCondition>();
  }

  [Fact]
  public void ALoopConfigWithTheLoopTypeMemberLastParses() {
    var loop = "{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{\"count\":3,\"loopType\":\"count\"},\"body\":[" + GamepackStepFixtures.PrimitiveOnly("s2") + "]}";
    var sequence = "{\"name\":\"S\",\"steps\":[" + loop + "]}";
    var json = "{\"manifest\":{\"name\":\"p\",\"version\":\"1\"},\"sequences\":[" + sequence + "],\"commands\":[],\"images\":[]}";

    var result = GamepackFileSerializer.Parse(json);

    result.Problems.Should().BeEmpty();
    result.File!.Sequences.Single().Steps.Single().Loop.Should().BeOfType<CountLoopConfig>().Which.Count.Should().Be(3);
  }
}
