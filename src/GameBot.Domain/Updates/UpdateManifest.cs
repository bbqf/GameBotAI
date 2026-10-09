using System.Text.Json;
using GameBot.Domain.Versioning;

namespace GameBot.Domain.Updates;

/// <summary>The manifest cannot be used. The code is an error code from the update API contract.</summary>
public sealed class UpdateManifestException : Exception {
  public UpdateManifestException() { Code = UpdateManifest.InvalidCode; }

  public UpdateManifestException(string message) : base(message) { Code = UpdateManifest.InvalidCode; }

  public UpdateManifestException(string message, Exception innerException) : base(message, innerException) { Code = UpdateManifest.InvalidCode; }

  public UpdateManifestException(string code, string message) : base(message) { Code = code; }

  public string Code { get; }
}

/// <summary>
/// The file update-manifest.json of a release (contract release-manifest.md).
/// The parser accepts schemaVersion 1 only.
/// </summary>
public sealed record UpdateManifest(SemanticVersion Version, string MsiFileName, long MsiSizeBytes, string MsiSha256) {
  public const int SupportedSchemaVersion = 1;
  public const string InvalidCode = "update_manifest_invalid";
  public const string UnsupportedCode = "update_manifest_unsupported";

  /// <summary>
  /// Parse the manifest text. The version must equal <paramref name="expectedVersion"/> (the version of the release tag).
  /// </summary>
  /// <exception cref="UpdateManifestException">The manifest is not valid or its schema version is not supported.</exception>
  public static UpdateManifest Parse(string json, SemanticVersion expectedVersion) {
    JsonDocument document;
    try {
      document = JsonDocument.Parse(json ?? string.Empty);
    }
    catch (JsonException ex) {
      throw new UpdateManifestException("The manifest is not valid JSON.", ex);
    }

    using (document) {
      var root = document.RootElement;
      if (root.ValueKind != JsonValueKind.Object) {
        throw new UpdateManifestException("The manifest must be a JSON object.");
      }

      var schema = ReadInt(root, "schemaVersion");
      if (schema > SupportedSchemaVersion) {
        throw new UpdateManifestException(UnsupportedCode, $"The manifest schema version {schema} is not supported.");
      }
      if (schema != SupportedSchemaVersion) {
        throw new UpdateManifestException($"The manifest schema version {schema} is not valid.");
      }

      var versionText = ReadString(root, "version");
      if (!SemanticVersion.TryParse(versionText, out var version)) {
        throw new UpdateManifestException("The manifest version is not valid.");
      }
      if (version != expectedVersion) {
        throw new UpdateManifestException("The manifest version does not match the release tag.");
      }

      if (!root.TryGetProperty("msi", out var msi) || msi.ValueKind != JsonValueKind.Object) {
        throw new UpdateManifestException("The manifest has no msi section.");
      }

      var fileName = ReadString(msi, "fileName");
      if (string.IsNullOrWhiteSpace(fileName)) {
        throw new UpdateManifestException("The manifest msi.fileName is missing.");
      }

      var size = ReadLong(msi, "sizeBytes");
      if (size <= 0) {
        throw new UpdateManifestException("The manifest msi.sizeBytes must be greater than 0.");
      }

      var sha = ReadString(msi, "sha256");
      if (!ChecksumVerifier.IsValidSha256Hex(sha)) {
        throw new UpdateManifestException("The manifest msi.sha256 must have 64 hexadecimal characters.");
      }

      return new UpdateManifest(version, fileName!, size, Convert.ToHexStringLower(Convert.FromHexString(sha!)));
    }
  }

  private static string? ReadString(JsonElement element, string name) =>
    element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

  private static int ReadInt(JsonElement element, string name) {
    if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)) {
      return number;
    }

    throw new UpdateManifestException($"The manifest field {name} is missing or is not a number.");
  }

  private static long ReadLong(JsonElement element, string name) {
    if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) {
      return number;
    }

    throw new UpdateManifestException($"The manifest field {name} is missing or is not a number.");
  }
}
