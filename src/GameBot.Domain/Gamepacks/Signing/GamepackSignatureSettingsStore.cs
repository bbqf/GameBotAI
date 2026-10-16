using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>
/// File-backed <see cref="IGamepackSignatureSettingsStore"/> (feature 146). It keeps <c>&lt;dataRoot&gt;/gamepack-signature.json</c>.
/// A read happens on each call. A write goes to a temporary file first and then replaces the file. When the file is absent,
/// the policy is off and the key list is empty. When the file cannot be read, the policy is required and the key list is
/// empty, so a damaged file never opens the gate.
/// </summary>
public sealed class GamepackSignatureSettingsStore : IGamepackSignatureSettingsStore {
  /// <summary>The file name in the data root.</summary>
  public const string FileName = "gamepack-signature.json";

  /// <summary>The largest label length.</summary>
  public const int MaxLabelLength = 100;

  private static readonly JsonSerializerOptions JsonOptions = new() {
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
  };

  private readonly string _path;
  private readonly object _lock = new();

  /// <summary>Creates the store on <paramref name="dataRoot"/>.</summary>
  /// <param name="dataRoot">The data root.</param>
  public GamepackSignatureSettingsStore(string dataRoot) {
    ArgumentException.ThrowIfNullOrWhiteSpace(dataRoot);
    _path = Path.Combine(dataRoot, FileName);
  }

  /// <summary>The path of the settings file.</summary>
  public string FilePath => _path;

  /// <inheritdoc />
  public GamepackSignatureSettings Load() {
    lock (_lock) {
      return LoadUnlocked();
    }
  }

  /// <inheritdoc />
  public SignaturePolicy GetPolicy() => Load().Policy;

  /// <inheritdoc />
  public void SetPolicy(SignaturePolicy policy) {
    lock (_lock) {
      var settings = LoadUnlocked();
      settings.Policy = policy;
      Save(settings);
    }
  }

  /// <inheritdoc />
  public AddKeyOutcome AddKey(string? label, string? publicKey) {
    var trimmed = label?.Trim();
    if (string.IsNullOrEmpty(trimmed) || trimmed.Length > MaxLabelLength) {
      return new AddKeyOutcome(AddKeyStatus.InvalidLabel, null);
    }

    if (!TryFingerprint(publicKey, out var fingerprint)) {
      return new AddKeyOutcome(AddKeyStatus.InvalidKey, null);
    }

    lock (_lock) {
      var settings = LoadUnlocked();
      if (settings.Keys.Any(k => string.Equals(k.Fingerprint, fingerprint, StringComparison.Ordinal))) {
        return new AddKeyOutcome(AddKeyStatus.Duplicate, null);
      }

      var key = new TrustedKey { Label = trimmed, Fingerprint = fingerprint, PublicKey = publicKey!.Trim() };
      settings.Keys.Add(key);
      Save(settings);
      return new AddKeyOutcome(AddKeyStatus.Added, key);
    }
  }

  /// <inheritdoc />
  public bool RemoveKey(string fingerprint) {
    ArgumentNullException.ThrowIfNull(fingerprint);
    lock (_lock) {
      var settings = LoadUnlocked();
      var existing = settings.Keys.FirstOrDefault(k => string.Equals(k.Fingerprint, fingerprint.Trim(), StringComparison.OrdinalIgnoreCase));
      if (existing is null) {
        return false;
      }

      settings.Keys.Remove(existing);

      Save(settings);
      return true;
    }
  }

  /// <summary>Gives the fingerprint of a key: SHA-256 of the SPKI bytes, in lower case hex. Gives false for a bad key.</summary>
  /// <param name="publicKey">The base64 SPKI text of an ECDSA P-256 key.</param>
  /// <param name="fingerprint">The fingerprint.</param>
  public static bool TryFingerprint(string? publicKey, out string fingerprint) {
    fingerprint = string.Empty;
    if (string.IsNullOrWhiteSpace(publicKey)) {
      return false;
    }

    try {
      var bytes = Convert.FromBase64String(publicKey.Trim());
      using var ecdsa = ECDsa.Create();
      ecdsa.ImportSubjectPublicKeyInfo(bytes, out _);
      var parameters = ecdsa.ExportParameters(includePrivateParameters: false);
      var oid = parameters.Curve.Oid;
      if (oid?.Value != "1.2.840.10045.3.1.7" && oid?.FriendlyName is not ("nistP256" or "ECDSA_P256")) {
        return false;
      }

      fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
      return true;
    }
    catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException) {
      return false;
    }
  }

  private GamepackSignatureSettings LoadUnlocked() {
    if (!File.Exists(_path)) {
      return new GamepackSignatureSettings();
    }

    try {
      var text = File.ReadAllText(_path, Encoding.UTF8);
      return JsonSerializer.Deserialize<GamepackSignatureSettings>(text, JsonOptions) ?? new GamepackSignatureSettings();
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException) {
      return new GamepackSignatureSettings { Policy = SignaturePolicy.Required };
    }
  }

  private void Save(GamepackSignatureSettings settings) {
    var folder = Path.GetDirectoryName(_path);
    if (!string.IsNullOrEmpty(folder)) {
      Directory.CreateDirectory(folder);
    }

    var temp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    File.WriteAllText(temp, JsonSerializer.Serialize(settings, JsonOptions), new UTF8Encoding(false));
    try {
      File.Move(temp, _path, overwrite: true);
    }
    catch {
      if (File.Exists(temp)) {
        File.Delete(temp);
      }

      throw;
    }
  }
}
