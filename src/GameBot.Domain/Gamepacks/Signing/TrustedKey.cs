namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>One trusted public key. The label is for display only and has no effect on trust.</summary>
public sealed class TrustedKey {
  /// <summary>The label, 1 to 100 characters.</summary>
  public string Label { get; set; } = string.Empty;

  /// <summary>The SHA-256 of the SPKI bytes, in lower case hex.</summary>
  public string Fingerprint { get; set; } = string.Empty;

  /// <summary>The ECDSA P-256 public key as base64 SubjectPublicKeyInfo.</summary>
  public string PublicKey { get; set; } = string.Empty;
}
