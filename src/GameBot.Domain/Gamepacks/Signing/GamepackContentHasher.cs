using System.Security.Cryptography;
using System.Text.Json;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>The hashes of a gamepack: the manifest and each sequence, command, and image.</summary>
/// <param name="ManifestSha256">The manifest hash.</param>
/// <param name="Files">The file entries, sorted by type and lower case name.</param>
public sealed record GamepackHashes(string ManifestSha256, IReadOnlyList<SignedFileEntry> Files);

/// <summary>
/// Hashes the raw JSON of a gamepack file in canonical form (feature 146). The hash reads the raw text, so a field that the
/// platform does not know is part of the hash.
/// </summary>
public static class GamepackContentHasher {
  /// <summary>Hashes a gamepack. Gives null when the text is not a JSON object.</summary>
  /// <param name="gamepackJson">The gamepack file text.</param>
  public static GamepackHashes? Compute(string gamepackJson) {
    ArgumentNullException.ThrowIfNull(gamepackJson);
    JsonDocument doc;
    try {
      doc = JsonDocument.Parse(gamepackJson);
    }
    catch (JsonException) {
      return null;
    }

    using (doc) {
      var root = doc.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        return null;
      }

      var manifest = FindProperty(root, "manifest");
      var manifestHash = manifest is { } m ? Hash(m) : HashNull();
      var files = new List<SignedFileEntry>();
      AddList(files, root, "sequences", GamepackObjectTypes.Sequence, "name");
      AddList(files, root, "commands", GamepackObjectTypes.Command, "name");
      AddList(files, root, "images", GamepackObjectTypes.Image, "id");
      return new GamepackHashes(manifestHash, SignatureFile.Sort(files));
    }
  }

  private static void AddList(List<SignedFileEntry> files, JsonElement root, string listName, string type, string nameField) {
    if (FindProperty(root, listName) is not { ValueKind: JsonValueKind.Array } list) {
      return;
    }

    foreach (var item in list.EnumerateArray()) {
      var name = item.ValueKind == JsonValueKind.Object && FindProperty(item, nameField) is { ValueKind: JsonValueKind.String } n
        ? n.GetString() ?? string.Empty
        : string.Empty;
      files.Add(new SignedFileEntry(type, name, Hash(item)));
    }
  }

  // Member names match without case, as in the gamepack parser.
  private static JsonElement? FindProperty(JsonElement element, string name) {
    foreach (var property in element.EnumerateObject()) {
      if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) {
        return property.Value;
      }
    }

    return null;
  }

  private static string Hash(JsonElement element) =>
    Convert.ToHexStringLower(SHA256.HashData(CanonicalJson.ToBytes(element)));

  private static string HashNull() =>
    Convert.ToHexStringLower(SHA256.HashData("null"u8));
}
