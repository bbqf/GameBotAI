// The normalized form of feature 145 (research R2). The export and the "unchanged" test of the import use it, so a
// second import of the same document writes nothing (FR-011).
//
// Fields found in the domain models (task T001) and how the normalizer handles each one:
//   CommandSequence.Id ................ removed.
//   CommandSequence.CreatedAt ......... removed (time stamp).
//   CommandSequence.UpdatedAt ......... removed (time stamp).
//   SequenceStep.CommandId ............ replaced by the command name (a name that is not found stays as it is).
//   SequenceStep.CommandReference.CommandId ... replaced by the command name.
//   Command.Id ........................ removed.
//   Command.TriggerId ................. removed (a trigger id belongs to this instance; triggers are not exported).
//   CommandStep.TargetId (type Command) ... replaced by the command name.
//   Image ids in steps and conditions . kept. The user chooses an image id, and it is the image name.
//   QueueTemplate.Id .................. removed.
//   QueueTemplate.CreatedAt/UpdatedAt . removed (time stamps).
//   QueueTemplateEntry.SequenceId ..... replaced by the sequence name (field "sequence").
//   ExecutionQueue.Id ................. removed.
//   ExecutionQueue.CreatedAt/UpdatedAt  removed (time stamps).
//   ExecutionQueue.LinkedTemplateId ... replaced by the template name (field "linkedTemplate").
//   ExecutionQueue.LinkedGameId ....... replaced by the game name (field "linkedGame").
//   QueueEntry.EntryId ................ removed. The queue keeps only the sequence names, in order.
//   QueueEntry.SequenceId ............. replaced by the sequence name.
// All JSON objects are written with the member names in ordinal order, so the same input gives the same output.

using System.Text.Json;
using System.Text.Json.Nodes;
using GameBot.Domain.Commands;
using GameBot.Domain.Gamepacks;

namespace GameBot.Domain.QueueSettings;

/// <summary>Makes the id-free, key-sorted, time-stamp-free form of sequences, commands, templates, and queues.</summary>
public static class SettingsNormalizer {
  private static readonly string[] SequenceRemoved = { "id", "createdAt", "updatedAt" };
  private static readonly string[] CommandRemoved = { "id", "triggerId" };

  /// <summary>Makes the document form of a stored sequence.</summary>
  /// <param name="sequence">The stored sequence.</param>
  /// <param name="commandName">Gives the command name for a command id, or null when the id is not known.</param>
  public static JsonObject SequenceToDoc(CommandSequence sequence, Func<string, string?> commandName) {
    ArgumentNullException.ThrowIfNull(sequence);
    ArgumentNullException.ThrowIfNull(commandName);
    var copy = CloneSequence(sequence);
    GamepackReferences.RewriteCommandIds(copy, commandName);
    return StripSequence(copy);
  }

  /// <summary>Makes the document form of a stored command.</summary>
  /// <param name="command">The stored command.</param>
  /// <param name="commandName">Gives the command name for a command id, or null when the id is not known.</param>
  public static JsonObject CommandToDoc(Command command, Func<string, string?> commandName) {
    ArgumentNullException.ThrowIfNull(command);
    ArgumentNullException.ThrowIfNull(commandName);
    var copy = CloneCommand(command);
    GamepackReferences.RewriteCommandIds(copy, commandName);
    return StripCommand(copy);
  }

  /// <summary>Reads a sequence from its document form. The names stay in the command id fields.</summary>
  /// <param name="doc">The document object.</param>
  /// <exception cref="JsonException">The object has a wrong shape.</exception>
  public static CommandSequence ReadSequence(JsonObject doc) {
    ArgumentNullException.ThrowIfNull(doc);
    try {
      return doc.Deserialize<CommandSequence>(GamepackFileSerializer.SequenceJsonOptions)
        ?? throw new JsonException("The sequence is empty.");
    }
    catch (InvalidOperationException ex) {
      throw new JsonException(ex.Message, ex);
    }
  }

  /// <summary>Reads a command from its document form. The names stay in the command step target fields.</summary>
  /// <param name="doc">The document object.</param>
  /// <exception cref="JsonException">The object has a wrong shape.</exception>
  public static Command ReadCommand(JsonObject doc) {
    ArgumentNullException.ThrowIfNull(doc);
    var copy = (JsonObject)doc.DeepClone();
    if (!HasKey(copy, "id")) {
      copy["id"] = string.Empty;
    }

    try {
      return copy.Deserialize<Command>(GamepackFileSerializer.CommandJsonOptions)
        ?? throw new JsonException("The command is empty.");
    }
    catch (InvalidOperationException ex) {
      throw new JsonException(ex.Message, ex);
    }
  }

  /// <summary>Gives the canonical text of a sequence document object. Equal text means equal content.</summary>
  /// <param name="doc">The document object.</param>
  public static string CanonicalSequence(JsonObject doc) => StripSequence(ReadSequence(doc)).ToJsonString();

  /// <summary>Gives the canonical text of a command document object. Equal text means equal content.</summary>
  /// <param name="doc">The document object.</param>
  public static string CanonicalCommand(JsonObject doc) => StripCommand(ReadCommand(doc)).ToJsonString();

  /// <summary>Gives the canonical text of a queue template document object.</summary>
  /// <param name="template">The template.</param>
  public static string Canonical(QueueTemplateDoc template) {
    ArgumentNullException.ThrowIfNull(template);
    return Sort(JsonSerializer.SerializeToNode(template, SettingsDocumentJson.Options))!.ToJsonString();
  }

  /// <summary>Gives the canonical text of the settings of a queue.</summary>
  /// <param name="queue">The queue settings.</param>
  /// <param name="includeEntries">False to leave the entries out.</param>
  public static string Canonical(QueueSettingsDoc queue, bool includeEntries) {
    ArgumentNullException.ThrowIfNull(queue);
    var node = JsonSerializer.SerializeToNode(queue, SettingsDocumentJson.Options)!.AsObject();
    if (!includeEntries) {
      node.Remove("entries");
    }

    return Sort(node)!.ToJsonString();
  }

  /// <summary>Copies a node and sorts the members of all objects in ordinal order. The order of a list stays.</summary>
  /// <param name="node">The node.</param>
  public static JsonNode? Sort(JsonNode? node) {
    switch (node) {
      case JsonObject obj: {
        var sorted = new JsonObject();
        foreach (var pair in obj.OrderBy(p => p.Key, StringComparer.Ordinal)) {
          sorted[pair.Key] = Sort(pair.Value);
        }

        return sorted;
      }

      case JsonArray array: {
        var copy = new JsonArray();
        foreach (var item in array) {
          copy.Add(Sort(item));
        }

        return copy;
      }

      case null:
        return null;
      default:
        return node.DeepClone();
    }
  }

  private static JsonObject StripSequence(CommandSequence sequence) {
    sequence.Id = string.Empty;
    sequence.CreatedAt = null;
    sequence.UpdatedAt = null;
    var node = JsonSerializer.SerializeToNode(sequence, GamepackFileSerializer.SequenceJsonOptions)!.AsObject();
    RemoveKeys(node, SequenceRemoved);
    return (JsonObject)Sort(node)!;
  }

  private static JsonObject StripCommand(Command command) {
    command.Id = string.Empty;
    command.TriggerId = null;
    var node = JsonSerializer.SerializeToNode(command, GamepackFileSerializer.CommandJsonOptions)!.AsObject();
    RemoveKeys(node, CommandRemoved);
    return (JsonObject)Sort(node)!;
  }

  private static CommandSequence CloneSequence(CommandSequence sequence) {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(sequence, GamepackFileSerializer.SequenceJsonOptions);
    return JsonSerializer.Deserialize<CommandSequence>(bytes, GamepackFileSerializer.SequenceJsonOptions)!;
  }

  private static Command CloneCommand(Command command) {
    var bytes = JsonSerializer.SerializeToUtf8Bytes(command, GamepackFileSerializer.CommandJsonOptions);
    return JsonSerializer.Deserialize<Command>(bytes, GamepackFileSerializer.CommandJsonOptions)!;
  }

  private static bool HasKey(JsonObject obj, string key) =>
    obj.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));

  private static void RemoveKeys(JsonObject obj, string[] keys) {
    foreach (var key in keys) {
      foreach (var match in obj.Where(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList()) {
        obj.Remove(match);
      }
    }
  }
}
