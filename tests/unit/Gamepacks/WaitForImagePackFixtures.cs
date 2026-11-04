using System.Text.Json;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 177: gamepack files with inline WaitForImage steps. The unit and contract test projects share this
/// file. The file text is the JSON text of a real pack. The contract tests send it to the install route.
/// </summary>
internal static class WaitForImagePackFixtures {
  public const string PackName = "wfi-pack";
  public const string SequenceName = "Wait for the city";
  public const string ImageId = "pns-city-anchor";

  /// <summary>The payload (JSON text) of a WaitForImage action with the main fields.</summary>
  public static string Payload(int timeoutMs = 3000, string imageId = ImageId, double confidence = 0.85) =>
    $"{{\"timeoutMs\":{timeoutMs},\"detectionTarget\":{{\"referenceImageId\":\"{imageId}\",\"confidence\":{confidence.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}}}";

  /// <summary>An Action step (API form <c>primitiveAction.payload</c>) with a WaitForImage action.</summary>
  public static string Step(string stepId, string payload) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"primitiveAction\":{{\"type\":\"WaitForImage\",\"payload\":{payload}}}}}";

  /// <summary>An Action step in the stored form (<c>action.parameters</c>) with a WaitForImage action.</summary>
  public static string StoredStep(string stepId, string payload) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"action\":{{\"type\":\"WaitForImage\",\"parameters\":{payload}}}}}";

  public static string Loop(string stepId, string inner) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Loop\",\"loop\":{{\"loopType\":\"count\",\"count\":2}},\"body\":[{inner}]}}";

  public static string IfBody(string stepId, string inner, string? elseInner = null) =>
    elseInner is null
      ? $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Conditional\",\"body\":[{inner}]}}"
      : $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Conditional\",\"body\":[{inner}],\"elseBody\":[{elseInner}]}}";

  /// <summary>A step of another type, with no WaitForImage action.</summary>
  public static string OtherStep(string stepId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"primitiveAction\":{{\"type\":\"WaitForStill\",\"payload\":{{\"timeoutMs\":2000}}}}}}";

  public const string AbsentPayload =
    "{\"timeoutMs\":4000,\"until\":\"absent\",\"absentCaptures\":3,\"detectionTarget\":{\"referenceImageId\":\"pns-city-anchor\",\"confidence\":0.9,\"offsetX\":4,\"offsetY\":-2,\"selectionStrategy\":\"FirstMatch\"}}";

  public const string RegionPayload =
    "{\"timeoutMs\":2500,\"detectionTarget\":{\"referenceImageId\":\"pns-city-anchor\",\"region\":{\"x\":10,\"y\":20,\"width\":300,\"height\":200}}}";

  /// <summary>The steps of the main fixture: top level, loop, if, else, three levels, absent, region, other.</summary>
  public static string[] MainSteps() => new[] {
    Step("top", Payload()),
    Loop("loop1", Step("inLoop", Payload(timeoutMs: 1100))),
    IfBody("if1", Step("inIf", Payload(timeoutMs: 1200)), Step("inElse", Payload(timeoutMs: 1300))),
    Loop("outer", IfBody("mid", Loop("inner", Step("deep", Payload(timeoutMs: 1400))))),
    Step("absent", AbsentPayload),
    Step("region", RegionPayload),
    OtherStep("other")
  };

  /// <summary>A pack with one sequence of the given steps (JSON texts).</summary>
  public static string PackWith(string version, params string[] steps) {
    var sequence = JsonSerializer.Deserialize<JsonElement>($"{{\"name\":\"{SequenceName}\",\"steps\":[{string.Join(",", steps)}]}}");
    return GamepackTestKit.Pack(PackName, version, sequences: new object[] { sequence });
  }

  /// <summary>The main fixture pack.</summary>
  public static string MainPack(string version = "1.0.0") => PackWith(version, MainSteps());
}
