using System.Text.Json;

namespace GameBot.Domain.Gamepacks.Catalog;

/// <summary>
/// Parses the catalog index document <c>index.json</c> (feature 147, contracts/catalog-index.md). A bad document gives
/// <c>index_invalid</c>. A bad version entry is skipped and the other entries stay.
/// </summary>
public static class GamepackCatalogIndexParser {
  /// <summary>The supported format version.</summary>
  public const int FormatVersion = 1;

  /// <summary>Parses the index.</summary>
  /// <param name="json">The index bytes as UTF-8 text.</param>
  public static IReadOnlyList<CatalogEntry> Parse(ReadOnlyMemory<byte> json) {
    try {
      using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
      return ParseDocument(document.RootElement);
    }
    catch (JsonException ex) {
      throw new CatalogException(CatalogFailure.IndexInvalid, "The catalog index is not valid JSON.", ex);
    }
  }

  /// <summary>Parses the index text.</summary>
  /// <param name="json">The index text.</param>
  public static IReadOnlyList<CatalogEntry> Parse(string json) {
    ArgumentNullException.ThrowIfNull(json);
    return Parse(System.Text.Encoding.UTF8.GetBytes(json));
  }

  private static List<CatalogEntry> ParseDocument(JsonElement root) {
    if (root.ValueKind != JsonValueKind.Object
        || !root.TryGetProperty("formatVersion", out var format)
        || format.ValueKind != JsonValueKind.Number
        || !format.TryGetInt32(out var formatNumber)
        || formatNumber != FormatVersion) {
      throw new CatalogException(CatalogFailure.IndexInvalid, "The catalog index has no supported formatVersion. The value must be 1.");
    }

    if (!root.TryGetProperty("gamepacks", out var packs) || packs.ValueKind != JsonValueKind.Array) {
      throw new CatalogException(CatalogFailure.IndexInvalid, "The catalog index has no gamepacks list.");
    }

    var result = new List<CatalogEntry>();
    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    foreach (var pack in packs.EnumerateArray()) {
      var name = ReadString(pack, "name");
      if (pack.ValueKind != JsonValueKind.Object || !GamepackNameRules.IsValid(name)
          || !pack.TryGetProperty("versions", out var versions) || versions.ValueKind != JsonValueKind.Array) {
        continue;
      }

      foreach (var entry in versions.EnumerateArray()) {
        var parsed = ParseVersion(name!, entry);
        if (parsed is not null && seen.Add(parsed.Name + "\n" + parsed.ParsedVersion)) {
          result.Add(parsed);
        }
      }
    }

    return result;
  }

  private static CatalogEntry? ParseVersion(string name, JsonElement entry) {
    if (entry.ValueKind != JsonValueKind.Object) {
      return null;
    }

    var version = ReadString(entry, "version")?.Trim();
    var file = ReadString(entry, "file");
    if (!CatalogVersion.TryParse(version, out var parsedVersion) || !CatalogLocation.IsSafeRelativePath(file)) {
      return null;
    }

    string? signature = null;
    if (HasValue(entry, "signature")) {
      signature = ReadString(entry, "signature");
      if (!CatalogLocation.IsSafeRelativePath(signature)) {
        return null;
      }
    }

    string? sha = null;
    if (HasValue(entry, "sha256")) {
      sha = ReadString(entry, "sha256");
      if (sha is null || sha.Length != 64 || !sha.All(char.IsAsciiHexDigit)) {
        return null;
      }
    }

    return new CatalogEntry(name, version!, parsedVersion, ReadString(entry, "changelog") ?? string.Empty, file!, signature, sha);
  }

  private static bool HasValue(JsonElement element, string property) =>
    element.TryGetProperty(property, out var value) && value.ValueKind != JsonValueKind.Null;

  private static string? ReadString(JsonElement element, string property) =>
    element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
      ? value.GetString()
      : null;
}
