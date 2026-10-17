using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GameBot.Domain.Commands;
using GameBot.Domain.QueueSettings;

namespace GameBot.Domain.Gamepacks;

/// <summary>The result of a file parse.</summary>
public sealed class GamepackParseResult {
  /// <summary>The parsed file, or null when the text is not valid JSON.</summary>
  public GamepackFile? File { get; init; }

  /// <summary>Structure problems found during the parse.</summary>
  public IReadOnlyList<GamepackProblem> Problems { get; init; } = Array.Empty<GamepackProblem>();

  /// <summary>True when the text is not valid JSON (HTTP 400).</summary>
  public bool InvalidJson { get; init; }

  /// <summary>True when the type member of a polymorphic object is missing or wrong (HTTP 400, feature 154).</summary>
  public bool InvalidType { get; init; }
}

/// <summary>
/// Reads and writes gamepack files (feature 143). A sequence entry uses the JSON of the stored sequence. A
/// command entry uses the JSON of the stored command. Names of members match without case. An enum can be a
/// number or a name.
/// </summary>
public static class GamepackFileSerializer {
  private static readonly JsonSerializerOptions SequenceOptions = BuildSequenceOptions();
  private static readonly JsonSerializerOptions CommandOptions = BuildCommandOptions();
  private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web);
  private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

  /// <summary>The JSON options for a sequence. The queue settings document (feature 145) uses the same options.</summary>
  internal static JsonSerializerOptions SequenceJsonOptions => SequenceOptions;

  /// <summary>The JSON options for a command. The queue settings document (feature 145) uses the same options.</summary>
  internal static JsonSerializerOptions CommandJsonOptions => CommandOptions;

  /// <summary>Parses the text of a gamepack file.</summary>
  /// <param name="json">The file text.</param>
  public static GamepackParseResult Parse(string json) {
    ArgumentNullException.ThrowIfNull(json);
    JsonDocument doc;
    try {
      doc = JsonDocument.Parse(json);
    }
    catch (JsonException) {
      return new GamepackParseResult { InvalidJson = true, Problems = new[] { Structure(GamepackObjectTypes.Manifest, null, "The body is not valid JSON.") } };
    }

    using (doc) {
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        return new GamepackParseResult { InvalidJson = true, Problems = new[] { Structure(GamepackObjectTypes.Manifest, null, "The body must be a JSON object.") } };
      }

      var problems = new List<GamepackProblem>();
      var state = new ParseState();
      var manifest = ReadManifest(root, problems);
      var sequences = ReadList<CommandSequence>(root, "sequences", GamepackObjectTypes.Sequence, SequenceOptions, problems, state, ConvertSequenceSteps);
      var commands = ReadList<Command>(root, "commands", GamepackObjectTypes.Command, CommandOptions, problems, state);
      CheckImageMembers(root, problems);
      var images = ReadList<GamepackImage>(root, "images", GamepackObjectTypes.Image, ManifestOptions, problems, state);
      var seeds = ReadList<QueueTemplateDoc>(root, "seedTemplates", GamepackObjectTypes.SeedTemplate, SettingsDocumentJson.Options, problems, state);
      return new GamepackParseResult {
        File = new GamepackFile { Manifest = manifest, Sequences = sequences, Commands = commands, Images = images, SeedTemplates = seeds },
        Problems = problems,
        InvalidType = state.InvalidType
      };
    }
  }

  /// <summary>Writes a gamepack file as indented JSON.</summary>
  /// <param name="file">The file.</param>
  public static string Serialize(GamepackFile file) {
    ArgumentNullException.ThrowIfNull(file);
    var root = new JsonObject {
      ["manifest"] = JsonSerializer.SerializeToNode(file.Manifest, ManifestOptions),
      ["sequences"] = new JsonArray(file.Sequences.Select(s => JsonSerializer.SerializeToNode(s, SequenceOptions)).ToArray()),
      ["commands"] = new JsonArray(file.Commands.Select(c => JsonSerializer.SerializeToNode(c, CommandOptions)).ToArray()),
      ["images"] = new JsonArray(file.Images.Select(i => JsonSerializer.SerializeToNode(i, ManifestOptions)).ToArray())
    };
    // The member is written only when it has items, so a file without seeds stays the same byte for byte (feature 150).
    if (file.SeedTemplates.Count > 0) {
      root["seedTemplates"] = new JsonArray(file.SeedTemplates.Select(t => JsonSerializer.SerializeToNode(t, SettingsDocumentJson.Options)).ToArray());
    }

    return root.ToJsonString(WriteOptions);
  }

  private static GamepackManifest? ReadManifest(JsonElement root, List<GamepackProblem> problems) {
    if (!root.TryGetProperty("manifest", out var element) || element.ValueKind == JsonValueKind.Null) {
      problems.Add(Structure(GamepackObjectTypes.Manifest, null, "The manifest is required."));
      return null;
    }

    try {
      return element.Deserialize<GamepackManifest>(ManifestOptions);
    }
    catch (JsonException) {
      problems.Add(Structure(GamepackObjectTypes.Manifest, null, "The manifest has a wrong shape."));
      return null;
    }
  }

  private static List<T> ReadList<T>(JsonElement root, string member, string type, JsonSerializerOptions options, List<GamepackProblem> problems, ParseState state,
    Func<JsonElement, List<GamepackProblem>, JsonNode?>? prepare = null)
    where T : class {
    var list = new List<T>();
    if (!TryGetMember(root, member, out var element) || element.ValueKind == JsonValueKind.Null) {
      return list;
    }

    if (element.ValueKind != JsonValueKind.Array) {
      problems.Add(Structure(type, null, $"The member '{member}' must be a list."));
      return list;
    }

    var index = 0;
    foreach (var item in element.EnumerateArray()) {
      var entryPath = $"$.{member}[{index}]";
      try {
        var node = prepare is null ? null : prepare(item, problems);
        var source = node is null ? item : JsonSerializer.SerializeToElement(node);
        var typeProblem = GamepackTypeDiscriminatorCheck.Find(source, typeof(T), options, entryPath);
        if (typeProblem is not null) {
          state.InvalidType = true;
          problems.Add(Structure(type, NameOf(item), $"Entry {index} of '{member}' has a wrong type member at {typeProblem.Path}: {typeProblem.ReasonText}."));
          index++;
          continue;
        }

        var value = prepare is null ? item.Deserialize<T>(options) : node.Deserialize<T>(options);
        if (value is null) {
          problems.Add(Structure(type, null, $"Entry {index} of '{member}' is empty."));
        }
        else {
          list.Add(value);
        }
      }
      catch (NotSupportedException ex) {
        // Safety net: the check above must catch each type member problem. This path reports the entry and the exception type only.
        state.InvalidType = true;
        problems.Add(Structure(type, NameOf(item), $"Entry {index} of '{member}' has a wrong type member at {entryPath}: {ex.GetType().Name}."));
      }
      catch (JsonException ex) {
        problems.Add(Structure(type, NameOf(item), $"Entry {index} of '{member}' has a wrong shape: {ex.Message}"));
      }
      catch (InvalidOperationException ex) {
        problems.Add(Structure(type, NameOf(item), $"Entry {index} of '{member}' has a wrong shape: {ex.Message}"));
      }
      catch (ArgumentException ex) {
        problems.Add(Structure(type, NameOf(item), $"Entry {index} of '{member}' has a wrong shape: {ex.Message}"));
      }

      index++;
    }

    return list;
  }

  private sealed class ParseState {
    public bool InvalidType { get; set; }
  }

  private static readonly string[] KnownImageMembers = { "id", "contentType", "filename", "dataBase64", "alternates" };

  // Feature 153: a member that the image type does not know is an error, so a typo is not lost without a message.
  // The check reads the raw JSON, because the typed read drops an unknown member.
  private static void CheckImageMembers(JsonElement root, List<GamepackProblem> problems) {
    if (!TryGetMember(root, "images", out var element) || element.ValueKind != JsonValueKind.Array) {
      return;
    }

    var index = 0;
    foreach (var item in element.EnumerateArray()) {
      if (item.ValueKind == JsonValueKind.Object) {
        var name = NameOf(item) ?? $"images[{index}]";
        foreach (var property in item.EnumerateObject()) {
          if (!KnownImageMembers.Any(k => string.Equals(k, property.Name, StringComparison.OrdinalIgnoreCase))) {
            problems.Add(Structure(GamepackObjectTypes.Image, name,
              $"Image '{name}' has an unknown member '{property.Name}'. Known members: {string.Join(", ", KnownImageMembers)}."));
          }
        }
      }

      index++;
    }
  }

  // Feature 151: converts the API form of the steps of a sequence to the stored form before the read.
  private static JsonNode? ConvertSequenceSteps(JsonElement item, List<GamepackProblem> problems) {
    var node = JsonNode.Parse(item.GetRawText());
    return node is JsonObject obj ? GamepackStepReader.Convert(obj, NameOf(item), problems) : node;
  }

  // Member names match without case (feature 150 uses it for seedTemplates).
  private static bool TryGetMember(JsonElement root, string member, out JsonElement element) {
    if (root.TryGetProperty(member, out element)) {
      return true;
    }

    foreach (var property in root.EnumerateObject()) {
      if (string.Equals(property.Name, member, StringComparison.OrdinalIgnoreCase)) {
        element = property.Value;
        return true;
      }
    }

    element = default;
    return false;
  }

  private static string? NameOf(JsonElement item) {
    if (item.ValueKind != JsonValueKind.Object) {
      return null;
    }

    foreach (var property in item.EnumerateObject()) {
      if ((string.Equals(property.Name, "name", StringComparison.OrdinalIgnoreCase)
          || string.Equals(property.Name, "id", StringComparison.OrdinalIgnoreCase))
          && property.Value.ValueKind == JsonValueKind.String) {
        return property.Value.GetString();
      }
    }

    return null;
  }

  private static GamepackProblem Structure(string type, string? name, string message) =>
    new(GamepackProblemCategories.Structure, type, name, message);

  private static JsonSerializerOptions BuildSequenceOptions() {
    // Feature 154: the type member can be at any position in an object.
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true, AllowOutOfOrderMetadataProperties = true };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }

  private static JsonSerializerOptions BuildCommandOptions() {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web) { AllowOutOfOrderMetadataProperties = true };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }
}
