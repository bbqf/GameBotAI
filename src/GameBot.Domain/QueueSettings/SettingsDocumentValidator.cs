using System.Text.Json;
using System.Text.Json.Nodes;
using GameBot.Domain.Parameters;

namespace GameBot.Domain.QueueSettings;

/// <summary>The result of a document check.</summary>
public sealed class SettingsValidationResult {
  /// <summary>The parsed document, or null when the check found errors.</summary>
  public SettingsDocument? Document { get; init; }

  /// <summary>All errors. The list is empty when the document is valid.</summary>
  public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

  /// <summary>True when the document is valid.</summary>
  public bool IsValid => Errors.Count == 0 && Document is not null;
}

/// <summary>
/// Checks a settings document before the import writes anything (feature 145, FR-009, FR-014). It checks the JSON
/// shape, the format version, the names, and the shape of each sequence and command. The check collects all errors.
/// </summary>
public static class SettingsDocumentValidator {
  /// <summary>Checks the text of a document.</summary>
  /// <param name="json">The body text.</param>
  public static SettingsValidationResult Validate(string json) {
    ArgumentNullException.ThrowIfNull(json);
    JsonNode? root;
    try {
      root = JsonNode.Parse(json);
    }
    catch (JsonException) {
      return Fail("The body is not valid JSON.");
    }

    if (root is not JsonObject rootObject) {
      return Fail("The body must be a JSON object.");
    }

    var versionNode = rootObject.FirstOrDefault(p => string.Equals(p.Key, "formatVersion", StringComparison.OrdinalIgnoreCase)).Value;
    if (versionNode is not JsonValue versionValue || !versionValue.TryGetValue<int>(out var version)) {
      return Fail("The member formatVersion is required and must be the number 1.");
    }

    if (version != SettingsDocument.CurrentFormatVersion) {
      return Fail($"The formatVersion {version} is not supported. The supported value is 1.");
    }

    SettingsDocument? document;
    try {
      document = rootObject.Deserialize<SettingsDocument>(SettingsDocumentJson.Options);
    }
    catch (JsonException ex) {
      return Fail("The document has a wrong shape: " + ex.Message);
    }

    if (document is null) {
      return Fail("The document is empty.");
    }

    var errors = new List<string>();
    CheckQueues(document, errors);
    CheckTemplates(document, errors);
    CheckObjects(document.Sequences, SettingsObjectTypes.Sequence, errors);
    CheckObjects(document.Commands, SettingsObjectTypes.Command, errors);
    return errors.Count == 0
      ? new SettingsValidationResult { Document = document }
      : new SettingsValidationResult { Errors = errors };
  }

  /// <summary>Reads the name member of a sequence or command document object, or null.</summary>
  /// <param name="doc">The document object.</param>
  public static string? NameOf(JsonObject doc) {
    ArgumentNullException.ThrowIfNull(doc);
    var node = doc.FirstOrDefault(p => string.Equals(p.Key, "name", StringComparison.OrdinalIgnoreCase)).Value;
    return node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;
  }

  private static SettingsValidationResult Fail(string message) => new() { Errors = new[] { message } };

  private static void CheckQueues(SettingsDocument document, List<string> errors) {
    CheckNames(document.Queues.Select(q => q?.Name), SettingsObjectTypes.Queue, errors);
    foreach (var queue in document.Queues.Where(q => q is not null)) {
      if (!string.IsNullOrWhiteSpace(queue.Name) && string.IsNullOrWhiteSpace(queue.EmulatorSerial)) {
        errors.Add($"The queue '{queue.Name}' needs a non-empty emulatorSerial.");
      }

      if (queue.Entries.Any(string.IsNullOrWhiteSpace)) {
        errors.Add($"The queue '{queue.Name}' has an entry with an empty sequence name.");
      }
    }
  }

  private static void CheckTemplates(SettingsDocument document, List<string> errors) {
    CheckNames(document.QueueTemplates.Select(t => t?.Name), SettingsObjectTypes.QueueTemplate, errors);
    foreach (var template in document.QueueTemplates.Where(t => t is not null)) {
      if (template.Entries.Any(e => e is null || string.IsNullOrWhiteSpace(e.Sequence))) {
        errors.Add($"The queue template '{template.Name}' has an entry with an empty sequence name.");
      }
    }
  }

  private static void CheckObjects(IReadOnlyList<JsonObject> objects, string type, List<string> errors) {
    CheckNames(objects.Select(o => o is null ? null : NameOf(o)), type, errors);
    foreach (var item in objects.Where(o => o is not null)) {
      var name = NameOf(item);
      if (string.IsNullOrWhiteSpace(name)) {
        continue;
      }

      try {
        IEnumerable<ParameterDeclaration> declarations = type == SettingsObjectTypes.Sequence
          ? SettingsNormalizer.ReadSequence(item).Parameters
          : SettingsNormalizer.ReadCommand(item).Parameters;
        var declarationErrors = ParameterNameRules.ValidateDeclarations(declarations);
        if (declarationErrors.Count > 0) {
          errors.Add($"The {type} '{name}' has invalid parameter declarations: {string.Join("; ", declarationErrors)}.");
        }
      }
      catch (JsonException ex) {
        errors.Add($"The {type} '{name}' has a wrong shape: {ex.Message}");
      }
    }
  }

  private static void CheckNames(IEnumerable<string?> names, string type, List<string> errors) {
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var reported = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var index = 0;
    foreach (var name in names) {
      if (string.IsNullOrWhiteSpace(name)) {
        errors.Add($"The {type} at position {index} has no name.");
      }
      else if (!seen.Add(name) && reported.Add(name)) {
        errors.Add($"The {type} name '{name}' is used more than once. A name must be unique, and case is ignored.");
      }

      index++;
    }
  }
}
