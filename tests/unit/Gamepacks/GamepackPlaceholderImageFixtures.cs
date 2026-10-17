using System.Text.Json;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 152: gamepack files with an image id that is a parameter placeholder. The unit and integration
/// test projects share this file.
/// </summary>
internal static class GamepackPlaceholderImageFixtures {
  public const string SequenceName = "PNS.NovaInteract";
  public const string ParameterName = "novaOptionImage";

  /// <summary>An imageVisible condition (JSON text) with the given image id.</summary>
  public static string Condition(string imageId) =>
    $"{{\"type\":\"imageVisible\",\"imageId\":\"{imageId}\",\"minSimilarity\":0.85,\"negate\":false}}";

  /// <summary>An Action-free step that has the condition in its <c>condition</c> member.</summary>
  public static string StepWithCondition(string imageId, string stepId = "s1") =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Command\",\"commandId\":\"cmd-1\",\"condition\":{Condition(imageId)}}}";

  /// <summary>A Loop step with a nested step that has the condition.</summary>
  public static string LoopBody(string imageId) =>
    $"{{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{{\"loopType\":\"count\",\"count\":2}},\"body\":[{StepWithCondition(imageId, "n1")}]}}";

  /// <summary>A Loop step whose body has a Break step with the condition.</summary>
  public static string LoopBreak(string imageId) =>
    $"{{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{{\"loopType\":\"count\",\"count\":2}},\"body\":[{{\"stepId\":\"b1\",\"stepType\":\"Break\",\"breakCondition\":{Condition(imageId)}}}]}}";

  /// <summary>An If step with the condition.</summary>
  public static string IfCondition(string imageId) =>
    $"{{\"stepId\":\"i1\",\"stepType\":\"If\",\"if\":{{\"condition\":{Condition(imageId)}}},\"body\":[]}}";

  /// <summary>An If step with a nested step that has the condition, in its else branch.</summary>
  public static string IfElse(string imageId) =>
    $"{{\"stepId\":\"i1\",\"stepType\":\"If\",\"if\":{{\"condition\":{Condition("known")}}},\"body\":[],\"elseBody\":[{StepWithCondition(imageId, "e1")}]}}";

  /// <summary>One sequence with the declared parameter names and the steps. A second sequence is optional.</summary>
  public static string Sequence(string[] parameters, params string[] steps) =>
    SequenceNamed(SequenceName, parameters, steps);

  /// <summary>A sequence with a name.</summary>
  public static string SequenceNamed(string name, string[] parameters, params string[] steps) {
    var declared = string.Join(",", System.Linq.Enumerable.Select(parameters, p => $"{{\"name\":\"{p}\",\"type\":\"Text\"}}"));
    return $"{{\"name\":\"{name}\",\"parameters\":[{declared}],\"steps\":[{string.Join(",", steps)}]}}";
  }

  /// <summary>A pack with the given sequences (JSON texts), the command <c>cmd-1</c>, and the image <c>known</c>.</summary>
  public static string Pack(params string[] sequences) {
    var parsed = new List<object>();
    foreach (var sequence in sequences) {
      parsed.Add(JsonSerializer.Deserialize<JsonElement>(sequence));
    }

    return GamepackTestKit.Pack("demo-pack", "1.0.0",
      sequences: parsed.ToArray(),
      commands: new[] { GamepackTestKit.Cmd("cmd-1", "Demo command") },
      images: new[] { GamepackTestKit.Img("known") });
  }

  /// <summary>The reproduction file of issue 326: a declared parameter used as the whole image id of a condition.</summary>
  public static string Issue326Pack(string declaredName = ParameterName) =>
    Pack(Sequence(new[] { declaredName }, StepWithCondition("{{" + ParameterName + "}}")));
}
