using System.Text.Json;

#pragma warning disable CA1707, CA1861, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks;

/// <summary>
/// Feature 151: gamepack files with Action steps in each accepted form. The unit, contract, and integration test
/// projects share this file.
/// </summary>
internal static class GamepackStepFixtures {
  public const string PackName = "demo-pack";
  public const string SequenceName = "Run the demo command";
  public const string CommandId = "cmd-1";
  public const string CommandName = "Demo command";
  public const string StepId = "s1";

  // ---- step builders (JSON text of one step) ----

  /// <summary>Action step with <c>primitiveAction</c> only.</summary>
  public static string PrimitiveOnly(string stepId = StepId, string commandId = CommandId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"{commandId}\"}}}}}}";

  /// <summary>Action step with <c>commandReference</c> only.</summary>
  public static string ReferenceOnly(string stepId = StepId, string commandId = CommandId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"commandReference\":{{\"commandId\":\"{commandId}\",\"commandName\":\"{CommandName}\",\"isResolved\":true}}}}";

  /// <summary>Action step with the step member <c>commandId</c> only.</summary>
  public static string CommandIdOnly(string stepId = StepId, string commandId = CommandId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"commandId\":\"{commandId}\"}}";

  /// <summary>Action step with all three forms. The issue 321 form.</summary>
  public static string All(string stepId = StepId, string commandId = CommandId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"commandId\":\"{commandId}\",\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"{commandId}\"}}}},\"commandReference\":{{\"commandId\":\"{commandId}\",\"commandName\":\"{CommandName}\"}}}}";

  /// <summary>Action step with <c>commandId</c> and <c>primitiveAction</c> and no <c>commandReference</c>.</summary>
  public static string CommandIdAndPrimitive(string stepId = StepId, string commandId = CommandId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"commandId\":\"{commandId}\",\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"{commandId}\"}}}}}}";

  /// <summary>Action step in the stored form (the export form).</summary>
  public static string Stored(string stepId = StepId, string commandId = CommandId) =>
    $"{{\"stepId\":\"{stepId}\",\"stepType\":\"Action\",\"commandId\":\"{commandId}\",\"action\":{{\"type\":\"command\",\"parameters\":{{\"commandId\":\"{commandId}\"}}}},\"commandReference\":{{\"commandId\":\"{commandId}\",\"commandName\":\"{CommandName}\"}}}}";

  // ---- conflict forms ----

  /// <summary><c>commandReference</c> and <c>primitiveAction</c> name different commands.</summary>
  public static string ReferenceVersusPrimitive() =>
    $"{{\"stepId\":\"{StepId}\",\"stepType\":\"Action\",\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"other\"}}}},\"commandReference\":{{\"commandId\":\"{CommandId}\"}}}}";

  /// <summary><c>commandId</c> and <c>primitiveAction</c> name different commands.</summary>
  public static string CommandIdVersusPrimitive() =>
    $"{{\"stepId\":\"{StepId}\",\"stepType\":\"Action\",\"commandId\":\"{CommandId}\",\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"other\"}}}}}}";

  /// <summary><c>action</c> and <c>primitiveAction</c> name different commands.</summary>
  public static string ActionVersusPrimitiveCommand() =>
    $"{{\"stepId\":\"{StepId}\",\"stepType\":\"Action\",\"action\":{{\"type\":\"command\",\"parameters\":{{\"commandId\":\"{CommandId}\"}}}},\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"other\"}}}}}}";

  /// <summary><c>action</c> and <c>primitiveAction</c> have different types.</summary>
  public static string ActionVersusPrimitiveType() =>
    $"{{\"stepId\":\"{StepId}\",\"stepType\":\"Action\",\"action\":{{\"type\":\"command\",\"parameters\":{{\"commandId\":\"{CommandId}\"}}}},\"primitiveAction\":{{\"type\":\"tap\",\"payload\":{{}}}}}}";

  /// <summary><c>action</c> and <c>primitiveAction</c> agree. <c>action</c> wins.</summary>
  public static string ActionAgreesWithPrimitive() =>
    $"{{\"stepId\":\"{StepId}\",\"stepType\":\"Action\",\"action\":{{\"type\":\"command\",\"parameters\":{{\"commandId\":\"{CommandId}\",\"extra\":\"kept\"}}}},\"primitiveAction\":{{\"type\":\"command\",\"payload\":{{\"commandId\":\"{CommandId}\"}}}}}}";

  // ---- unresolvable command (not in the file, not on the system) ----

  public static string UnresolvablePrimitive() => PrimitiveOnly(commandId: "missing-cmd");

  public static string UnresolvableReference() => ReferenceOnly(commandId: "missing-cmd");

  public static string UnresolvableCommandId() => CommandIdOnly(commandId: "missing-cmd");

  // ---- unknown members and aliases on other step types ----

  public static string UnknownMember() =>
    $"{{\"stepId\":\"{StepId}\",\"stepType\":\"Action\",\"commandId\":\"{CommandId}\",\"mystery\":1}}";

  public static string LoopWith(string alias) =>
    $"{{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{{\"loopType\":\"count\",\"count\":2}},{alias},\"body\":[]}}";

  public static string ConditionalWith(string alias) =>
    $"{{\"stepId\":\"C1\",\"stepType\":\"Conditional\",{alias},\"body\":[]}}";

  public static string CommandStepWith(string alias) =>
    $"{{\"stepId\":\"M1\",\"stepType\":\"Command\",\"commandId\":\"{CommandId}\",{alias}}}";

  public const string PrimitiveAlias = "\"primitiveAction\":{\"type\":\"command\",\"payload\":{\"commandId\":\"cmd-1\"}}";
  public const string ReferenceAlias = "\"commandReference\":{\"commandId\":\"cmd-1\"}";
  public const string CommandIdAlias = "\"commandId\":\"cmd-1\"";

  // ---- nesting ----

  public static string InLoop(string inner) =>
    $"{{\"stepId\":\"L1\",\"stepType\":\"Loop\",\"loop\":{{\"loopType\":\"count\",\"count\":2}},\"body\":[{inner}]}}";

  public static string InConditionalBody(string inner) =>
    $"{{\"stepId\":\"C1\",\"stepType\":\"Conditional\",\"body\":[{inner}]}}";

  public static string InConditionalElseBody(string inner) =>
    $"{{\"stepId\":\"C1\",\"stepType\":\"Conditional\",\"body\":[],\"elseBody\":[{inner}]}}";

  // ---- pack builders ----

  /// <summary>A pack with one sequence of the given steps (JSON texts) and the command <see cref="CommandId"/>.</summary>
  public static string PackWith(params string[] steps) => PackWithCommand(CommandId, steps);

  /// <summary>A pack like <see cref="PackWith"/> with a given command id.</summary>
  public static string PackWithCommand(string commandId, params string[] steps) {
    var sequence = JsonSerializer.Deserialize<JsonElement>($"{{\"name\":\"{SequenceName}\",\"steps\":[{string.Join(",", steps)}]}}");
    return GamepackTestKit.Pack(PackName, "1.0.0",
      sequences: new object[] { sequence },
      commands: new[] { GamepackTestKit.Cmd(commandId, CommandName) });
  }

  /// <summary>The reproduction file of issue 321: one sequence, one Action step, the command in the same file.</summary>
  public static string Issue321Pack() => PackWith(All());
}
