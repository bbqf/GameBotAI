using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GameBot.Domain.Commands;

namespace GameBot.Domain.Gamepacks;

/// <summary>The result of a file parse.</summary>
public sealed class GamepackParseResult {
  /// <summary>The parsed file, or null when the text is not valid JSON.</summary>
  public GamepackFile? File { get; init; }

  /// <summary>Structure problems found during the parse.</summary>
  public IReadOnlyList<GamepackProblem> Problems { get; init; } = Array.Empty<GamepackProblem>();

  /// <summary>True when the text is not valid JSON (HTTP 400).</summary>
  public bool InvalidJson { get; init; }
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
      var manifest = ReadManifest(root, problems);
      var sequences = ReadList<CommandSequence>(root, "sequences", GamepackObjectTypes.Sequence, SequenceOptions, problems);
      var commands = ReadList<Command>(root, "commands", GamepackObjectTypes.Command, CommandOptions, problems);
      var images = ReadList<GamepackImage>(root, "images", GamepackObjectTypes.Image, ManifestOptions, problems);
      return new GamepackParseResult {
        File = new GamepackFile { Manifest = manifest, Sequences = sequences, Commands = commands, Images = images },
        Problems = problems
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

  private static List<T> ReadList<T>(JsonElement root, string member, string type, JsonSerializerOptions options, List<GamepackProblem> problems)
    where T : class {
    var list = new List<T>();
    if (!root.TryGetProperty(member, out var element) || element.ValueKind == JsonValueKind.Null) {
      return list;
    }

    if (element.ValueKind != JsonValueKind.Array) {
      problems.Add(Structure(type, null, $"The member '{member}' must be a list."));
      return list;
    }

    var index = 0;
    foreach (var item in element.EnumerateArray()) {
      try {
        var value = item.Deserialize<T>(options);
        if (value is null) {
          problems.Add(Structure(type, null, $"Entry {index} of '{member}' is empty."));
        }
        else {
          list.Add(value);
        }
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
    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }

  private static JsonSerializerOptions BuildCommandOptions() {
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    options.Converters.Add(new JsonStringEnumConverter());
    return options;
  }
}
