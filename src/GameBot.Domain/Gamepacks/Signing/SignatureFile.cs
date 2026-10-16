using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>One signed file entry: the type, the name, and the SHA-256 hash.</summary>
/// <param name="Type">One of sequence, command, or image.</param>
/// <param name="Name">The name of a sequence or command, or the id of an image.</param>
/// <param name="Sha256">The lower case hex hash of the canonical JSON of the entry.</param>
public sealed record SignedFileEntry(string Type, string Name, string Sha256);

/// <summary>The detached signature file of a gamepack, parsed (feature 146).</summary>
public sealed partial class SignatureFile {
  /// <summary>The only known format text.</summary>
  public const string FormatName = "gamepack-signature-1";

  /// <summary>The only known algorithm text.</summary>
  public const string AlgorithmName = "ecdsa-p256-sha256";

  /// <summary>The format text.</summary>
  public string Format { get; private init; } = string.Empty;

  /// <summary>The algorithm text.</summary>
  public string Algorithm { get; private init; } = string.Empty;

  /// <summary>The base64 SPKI text of the signer key. It is used for the crypto test only.</summary>
  public string PublicKey { get; private init; } = string.Empty;

  /// <summary>The hash of the canonical manifest.</summary>
  public string ManifestSha256 { get; private init; } = string.Empty;

  /// <summary>The signed file entries.</summary>
  public IReadOnlyList<SignedFileEntry> Files { get; private init; } = Array.Empty<SignedFileEntry>();

  /// <summary>The signature bytes (r then s, 64 bytes).</summary>
  public ReadOnlyMemory<byte> Signature { get; private init; }

  /// <summary>
  /// Reads and checks the text of a signature file. Gives null for an empty text, a text that is not JSON, an unknown
  /// format or algorithm, a bad encoding, or a duplicate entry.
  /// </summary>
  /// <param name="text">The text.</param>
  public static SignatureFile? TryParse(string? text) {
    if (string.IsNullOrWhiteSpace(text)) {
      return null;
    }

    try {
      using var doc = JsonDocument.Parse(text);
      return ParseRoot(doc.RootElement);
    }
    catch (Exception ex) when (ex is JsonException or FormatException or InvalidOperationException) {
      return null;
    }
  }

  /// <summary>Gives the bytes that the signer signs: the canonical JSON of format, manifestSha256, and the sorted files.</summary>
  /// <param name="manifestSha256">The manifest hash.</param>
  /// <param name="files">The file entries.</param>
  public static byte[] BuildSignedBytes(string manifestSha256, IEnumerable<SignedFileEntry> files) {
    ArgumentNullException.ThrowIfNull(manifestSha256);
    ArgumentNullException.ThrowIfNull(files);
    var list = new JsonArray();
    foreach (var entry in Sort(files)) {
      list.Add(new JsonObject { ["name"] = entry.Name, ["sha256"] = entry.Sha256, ["type"] = entry.Type });
    }

    var root = new JsonObject { ["files"] = list, ["format"] = FormatName, ["manifestSha256"] = manifestSha256 };
    using var doc = JsonDocument.Parse(root.ToJsonString());
    return CanonicalJson.ToBytes(doc.RootElement);
  }

  /// <summary>Gives the signed bytes of this file.</summary>
  public byte[] SignedBytes() => BuildSignedBytes(ManifestSha256, Files);

  /// <summary>Sorts entries by type, then by lower case name, both ordinal.</summary>
  /// <param name="files">The entries.</param>
#pragma warning disable CA1308 // The signature contract sorts by the lower case name.
  public static IReadOnlyList<SignedFileEntry> Sort(IEnumerable<SignedFileEntry> files) {
    ArgumentNullException.ThrowIfNull(files);
    return files
      .OrderBy(f => f.Type, StringComparer.Ordinal)
      .ThenBy(f => f.Name.ToLowerInvariant(), StringComparer.Ordinal)
      .ToList();
  }
#pragma warning restore CA1308

  private static SignatureFile? ParseRoot(JsonElement root) {
    if (root.ValueKind != JsonValueKind.Object) {
      return null;
    }

    var format = ReadString(root, "format");
    var algorithm = ReadString(root, "algorithm");
    var publicKey = ReadString(root, "publicKey");
    var manifest = ReadString(root, "manifestSha256");
    var signature = ReadString(root, "signature");
    if (format != FormatName || algorithm != AlgorithmName || string.IsNullOrEmpty(publicKey)
        || manifest is null || !HexPattern().IsMatch(manifest) || string.IsNullOrEmpty(signature)) {
      return null;
    }

    if (!root.TryGetProperty("files", out var filesElement) || filesElement.ValueKind != JsonValueKind.Array) {
      return null;
    }

    var files = new List<SignedFileEntry>();
    var seen = new HashSet<string>(StringComparer.Ordinal);
    foreach (var item in filesElement.EnumerateArray()) {
      if (item.ValueKind != JsonValueKind.Object) {
        return null;
      }

      var type = ReadString(item, "type");
      var name = ReadString(item, "name");
      var hash = ReadString(item, "sha256");
      if (type is not (GamepackObjectTypes.Sequence or GamepackObjectTypes.Command or GamepackObjectTypes.Image)
          || string.IsNullOrEmpty(name) || hash is null || !HexPattern().IsMatch(hash)
          || !seen.Add(type + "\n" + name.ToUpperInvariant())) {
        return null;
      }

      files.Add(new SignedFileEntry(type, name, hash));
    }

    var signatureBytes = Convert.FromBase64String(signature);
    if (signatureBytes.Length != 64) {
      return null;
    }

    return new SignatureFile {
      Format = format,
      Algorithm = algorithm,
      PublicKey = publicKey,
      ManifestSha256 = manifest,
      Files = files,
      Signature = signatureBytes
    };
  }

  private static string? ReadString(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

  [GeneratedRegex("^[0-9a-f]{64}$")]
  private static partial Regex HexPattern();
}
