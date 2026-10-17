using System.Text.Json;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 154: gamepack files with a condition whose type member is at a chosen position. The unit, contract, and
/// integration test projects share this file.
/// </summary>
internal static class GamepackMemberOrderFixtures {
  public const string ConditionFirst = "{\"type\":\"commandOutcome\",\"stepRef\":\"s0\",\"expectedState\":\"success\",\"negate\":false}";
  public const string ConditionMiddle = "{\"stepRef\":\"s0\",\"type\":\"commandOutcome\",\"expectedState\":\"success\",\"negate\":false}";
  public const string ConditionLast = "{\"expectedState\":\"success\",\"negate\":false,\"stepRef\":\"s0\",\"type\":\"commandOutcome\"}";

  /// <summary>A pack with one sequence: an Action step s0 and a Conditional step that holds the given condition JSON.</summary>
  public static string PackWithCondition(string conditionJson, string sequenceName = GamepackStepFixtures.SequenceName) {
    var conditional = $"{{\"stepId\":\"c1\",\"stepType\":\"Conditional\",\"condition\":{conditionJson},\"body\":[{GamepackStepFixtures.PrimitiveOnly("s2")}]}}";
    var steps = new[] { GamepackStepFixtures.PrimitiveOnly("s0"), conditional };
    var sequence = JsonSerializer.Deserialize<JsonElement>($"{{\"name\":\"{sequenceName}\",\"steps\":[{string.Join(",", steps)}]}}");
    return GamepackTestKit.Pack(GamepackStepFixtures.PackName, "1.0.0",
      sequences: new object[] { sequence },
      commands: new[] { GamepackTestKit.Cmd(GamepackStepFixtures.CommandId, GamepackStepFixtures.CommandName) });
  }

  /// <summary>A pack whose step 0 is a loop with a break condition of type all. Item 1 of the list has the given JSON.</summary>
  public static string PackWithNestedBreak(string secondItemJson) {
    const string first = "{\"type\":\"imageVisible\",\"imageId\":\"img\",\"negate\":false}";
    var loop = "{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{\"loopType\":\"count\",\"count\":2},"
      + $"\"breakCondition\":{{\"type\":\"all\",\"children\":[{first},{secondItemJson}]}},\"body\":[{GamepackStepFixtures.PrimitiveOnly("s2")}]}}";
    var sequence = JsonSerializer.Deserialize<JsonElement>($"{{\"name\":\"{GamepackStepFixtures.SequenceName}\",\"steps\":[{loop}]}}");
    return GamepackTestKit.Pack(GamepackStepFixtures.PackName, "1.0.0",
      sequences: new object[] { sequence },
      commands: new[] { GamepackTestKit.Cmd(GamepackStepFixtures.CommandId, GamepackStepFixtures.CommandName) });
  }

  public const string NestedBadItem = "{\"imageId\":\"img\",\"negate\":false,\"type\":\"nope\"}";

  public static string MissingType() => "{\"stepRef\":\"s0\",\"expectedState\":\"success\"}";
  public static string UnknownType() => "{\"type\":\"nope\",\"stepRef\":\"s0\",\"expectedState\":\"success\"}";
  public static string NumberType() => "{\"type\":5,\"stepRef\":\"s0\",\"expectedState\":\"success\"}";
  public static string DuplicateType() => "{\"type\":\"commandOutcome\",\"stepRef\":\"s0\",\"expectedState\":\"success\",\"type\":\"commandOutcome\"}";
}
