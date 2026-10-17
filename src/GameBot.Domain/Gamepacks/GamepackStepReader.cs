using System.Text.Json.Nodes;
using GameBot.Domain.Actions;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Gamepacks;

/// <summary>
/// Converts the steps of a sequence in a gamepack file to the stored form (feature 151). A file author can write an
/// Action step in the API form (<c>primitiveAction</c> with <c>payload</c>, <c>commandReference</c>, or a step member
/// <c>commandId</c>). The stored step has the members <c>action</c> with <c>parameters</c>, <c>commandId</c>, and
/// <c>commandReference</c>. The reader converts the API form, and it reports each step member that the install cannot keep.
/// </summary>
public static class GamepackStepReader {
  private const string PrimitiveActionName = "primitiveAction";

  // The members of the stored step (SequenceStep). Names match without case.
  private static readonly HashSet<string> StoredMembers = new(StringComparer.OrdinalIgnoreCase) {
    "order", "stepId", "label", "commandId", "commandReference", "stepType", "action", "waitForImage", "condition",
    "conditionExpression", "delayMs", "delayRangeMs", "timeoutMs", "retry", "gate", "requireDispatch", "loop", "body",
    "if", "elseBody", "breakCondition", "parameterBindings"
  };

  /// <summary>Converts the steps of a sequence. The input does not change.</summary>
  /// <param name="sequence">The JSON object of the sequence entry.</param>
  /// <param name="sequenceName">The sequence name, for the messages. It can be null.</param>
  /// <param name="problems">The list that receives a <c>structure</c> problem for each step that the install cannot keep.</param>
  /// <returns>A converted copy of the sequence.</returns>
  public static JsonObject Convert(JsonObject sequence, string? sequenceName, ICollection<GamepackProblem> problems) {
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(problems);
    var copy = (JsonObject)sequence.DeepClone();
    var context = new Context(sequenceName, problems);
    ConvertList(Find(copy, "steps"), context);
    return copy;
  }

  private sealed record Context(string? SequenceName, ICollection<GamepackProblem> Problems) {
    public void Add(string stepId, string message) =>
      Problems.Add(new GamepackProblem(GamepackProblemCategories.Structure, GamepackObjectTypes.Sequence, SequenceName,
        $"Step '{stepId}': {message}"));
  }

  private static void ConvertList(JsonNode? list, Context context) {
    if (list is not JsonArray array) {
      return;
    }

    foreach (var item in array) {
      if (item is JsonObject step) {
        ConvertStep(step, context);
      }
    }
  }

  private static void ConvertStep(JsonObject step, Context context) {
    var stepId = GetString(step, "stepId") ?? "(no id)";
    ConvertList(Find(step, "body"), context);
    ConvertList(Find(step, "elseBody"), context);

    foreach (var property in step) {
      if (!StoredMembers.Contains(property.Key) && !string.Equals(property.Key, PrimitiveActionName, StringComparison.OrdinalIgnoreCase)) {
        context.Add(stepId, $"the member '{property.Key}' is not a step member. The install cannot keep it.");
      }
    }

    var kind = ReadKind(step, out var explicitType);
    switch (kind) {
      case Kind.Invalid:
        return;
      case Kind.Action:
        ConvertAction(step, stepId, explicitType, context);
        return;
      default:
        CheckAliasesOnOtherStep(step, stepId, kind, context);
        return;
    }
  }

  private enum Kind { Action, Command, OtherType, Invalid }

  private static Kind ReadKind(JsonObject step, out bool explicitType) {
    var node = Find(step, "stepType");
    explicitType = node is not null;
    if (node is null) {
      return Kind.Action;
    }

    if (node is JsonValue value) {
      SequenceStepType parsed;
      if (value.TryGetValue<string>(out var text)) {
        if (!Enum.TryParse(text, true, out parsed)) {
          return Kind.Invalid;
        }
      }
      else if (value.TryGetValue<int>(out var number)) {
        parsed = (SequenceStepType)number;
      }
      else {
        return Kind.Invalid;
      }

      return parsed switch {
        SequenceStepType.Action => Kind.Action,
        SequenceStepType.Command => Kind.Command,
        _ => Kind.OtherType
      };
    }

    return Kind.Invalid;
  }

  // An alias is an unknown member on a step that is not an Action step. A null or empty value counts as absent,
  // because the export writes such values for steps of other types.
  private static void CheckAliasesOnOtherStep(JsonObject step, string stepId, Kind kind, Context context) {
    if (HasValue(Find(step, PrimitiveActionName))) {
      context.Add(stepId, $"the member '{PrimitiveActionName}' is only for an Action step. The install cannot keep it.");
    }

    if (HasValue(Find(step, "commandReference"))) {
      context.Add(stepId, "the member 'commandReference' is only for an Action step. The install cannot keep it.");
    }

    if (kind != Kind.Command && HasValue(Find(step, "commandId"))) {
      context.Add(stepId, "the member 'commandId' is only for an Action step or a Command step. The install cannot keep it.");
    }
  }

  private static void ConvertAction(JsonObject step, string stepId, bool explicitType, Context context) {
    var primitive = Find(step, PrimitiveActionName) as JsonObject;
    var reference = Find(step, "commandReference") as JsonObject;
    var stored = Find(step, "action") as JsonObject;
    var commandIdText = GetString(step, "commandId");
    if (string.IsNullOrWhiteSpace(commandIdText)) {
      commandIdText = null;
    }

    if (primitive is null && reference is null && commandIdText is null) {
      // No alias. The step is in the stored form already. Remove the unused null alias, if any.
      Remove(step, PrimitiveActionName);
      return;
    }

    var primitiveType = primitive is null ? null : GetString(primitive, "type");
    var payload = primitive is null ? null : Find(primitive, "payload") as JsonObject;
    var isCommandAction = string.Equals(primitiveType, ActionTypes.Command, StringComparison.OrdinalIgnoreCase);
    var primitiveId = isCommandAction ? NullIfBlank(GetString(payload, "commandId")) : null;
    var referenceId = NullIfBlank(GetString(reference, "commandId"));
    var referenceName = GetString(reference, "commandName");

    var sources = new List<(string Field, string Id)>();
    if (primitiveId is not null) {
      sources.Add(("primitiveAction.payload.commandId", primitiveId));
    }

    if (referenceId is not null) {
      sources.Add(("commandReference.commandId", referenceId));
    }

    if (commandIdText is not null) {
      sources.Add(("commandId", commandIdText));
    }

    if (sources.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1) {
      context.Add(stepId, "these members name different commands: " + string.Join(", ", sources.Select(s => $"{s.Field} '{s.Id}'")) + ". Use one command.");
      return;
    }

    if (stored is not null && primitive is not null && !StoredAgreesWithPrimitive(stored, primitiveType, primitiveId, stepId, context)) {
      return;
    }

    if (stored is not null) {
      // The stored form wins and stays as given.
      Remove(step, PrimitiveActionName);
      return;
    }

    var id = primitiveId ?? referenceId ?? commandIdText;
    if (primitive is not null) {
      var action = new JsonObject { ["type"] = primitiveType };
      var schema = GetString(primitive, "schemaVersion");
      if (schema is not null) {
        action["schemaVersion"] = schema;
      }

      var parameters = payload is null ? new JsonObject() : (JsonObject)payload.DeepClone();
      if (isCommandAction && id is not null) {
        Set(parameters, "commandId", id);
      }

      action["parameters"] = parameters;
      Set(step, "action", action);
    }
    else if (id is not null) {
      Set(step, "action", new JsonObject {
        ["type"] = ActionTypes.Command,
        ["parameters"] = new JsonObject { ["commandId"] = id }
      });
    }

    if (id is not null) {
      Set(step, "commandId", id);
      var newReference = new JsonObject { ["commandId"] = id };
      if (referenceName is not null) {
        newReference["commandName"] = referenceName;
      }

      Set(step, "commandReference", newReference);
    }

    Remove(step, PrimitiveActionName);
    if (!explicitType) {
      Set(step, "stepType", SequenceStepType.Action.ToString());
    }
  }

  private static bool StoredAgreesWithPrimitive(JsonObject stored, string? primitiveType, string? primitiveId, string stepId, Context context) {
    var storedType = GetString(stored, "type");
    if (!string.Equals(storedType, primitiveType, StringComparison.OrdinalIgnoreCase)) {
      context.Add(stepId, $"the members 'action' (type '{storedType}') and 'primitiveAction' (type '{primitiveType}') differ. Use one of them.");
      return false;
    }

    var storedId = NullIfBlank(GetString(Find(stored, "parameters") as JsonObject, "commandId"));
    if (primitiveId is not null && storedId is not null && !string.Equals(primitiveId, storedId, StringComparison.OrdinalIgnoreCase)) {
      context.Add(stepId, $"the members 'action' (command '{storedId}') and 'primitiveAction' (command '{primitiveId}') name different commands. Use one command.");
      return false;
    }

    return true;
  }

  // ---- JSON helpers (names match without case) ----

  private static JsonNode? Find(JsonObject? obj, string name) {
    if (obj is null) {
      return null;
    }

    foreach (var property in obj) {
      if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase)) {
        return property.Value;
      }
    }

    return null;
  }

  private static string? GetString(JsonObject? obj, string name) =>
    Find(obj, name) is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

  private static string? NullIfBlank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text;

  private static bool HasValue(JsonNode? node) =>
    node is not null && !(node is JsonValue value && value.TryGetValue<string>(out var text) && string.IsNullOrEmpty(text));

  private static void Remove(JsonObject obj, string name) {
    foreach (var key in obj.Select(p => p.Key).Where(k => string.Equals(k, name, StringComparison.OrdinalIgnoreCase)).ToList()) {
      obj.Remove(key);
    }
  }

  private static void Set(JsonObject obj, string name, JsonNode? value) {
    Remove(obj, name);
    obj[name] = value;
  }
}
