using System.Security.Cryptography;

namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>Checks the signature of a gamepack against a list of trusted keys (feature 146).</summary>
public interface IGamepackSignatureVerifier {
  /// <summary>Gives the status of a gamepack. The order of faults is: missing, invalid, content mismatch, untrusted.</summary>
  /// <param name="gamepackJson">The gamepack file text.</param>
  /// <param name="signatureText">The signature file text, or null when none was sent.</param>
  /// <param name="trustedKeys">The trusted keys.</param>
  SignatureStatus Verify(string gamepackJson, string? signatureText, IReadOnlyCollection<TrustedKey> trustedKeys);
}

/// <summary>
/// The ECDSA P-256 verifier. The key in the signature file is only an input for the crypto test. Trust is only the
/// fact that the fingerprint of that key is on the trusted list.
/// </summary>
public sealed class GamepackSignatureVerifier : IGamepackSignatureVerifier {
  /// <inheritdoc />
  public SignatureStatus Verify(string gamepackJson, string? signatureText, IReadOnlyCollection<TrustedKey> trustedKeys) {
    ArgumentNullException.ThrowIfNull(gamepackJson);
    ArgumentNullException.ThrowIfNull(trustedKeys);
    if (signatureText is null) {
      return SignatureStatus.Missing;
    }

    var file = SignatureFile.TryParse(signatureText);
    if (file is null || !CryptoTestPasses(file, out var fingerprint)) {
      return SignatureStatus.Invalid;
    }

    var hashes = GamepackContentHasher.Compute(gamepackJson);
    if (hashes is null || !Matches(file, hashes)) {
      return SignatureStatus.ContentMismatch;
    }

    return trustedKeys.Any(k => string.Equals(k.Fingerprint, fingerprint, StringComparison.Ordinal))
      ? SignatureStatus.Valid
      : SignatureStatus.Untrusted;
  }

  private static bool CryptoTestPasses(SignatureFile file, out string fingerprint) {
    fingerprint = string.Empty;
    if (!GamepackSignatureSettingsStore.TryFingerprint(file.PublicKey, out fingerprint)) {
      return false;
    }

    try {
      using var ecdsa = ECDsa.Create();
      ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(file.PublicKey.Trim()), out _);
      return ecdsa.VerifyData(file.SignedBytes(), file.Signature.Span, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
    catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException) {
      return false;
    }
  }

  private static bool Matches(SignatureFile file, GamepackHashes hashes) {
    if (!string.Equals(file.ManifestSha256, hashes.ManifestSha256, StringComparison.Ordinal)) {
      return false;
    }

    var signed = SignatureFile.Sort(file.Files);
    if (signed.Count != hashes.Files.Count) {
      return false;
    }

    for (var i = 0; i < signed.Count; i++) {
      if (signed[i].Type != hashes.Files[i].Type
          || !string.Equals(signed[i].Name, hashes.Files[i].Name, StringComparison.OrdinalIgnoreCase)
          || !string.Equals(signed[i].Sha256, hashes.Files[i].Sha256, StringComparison.Ordinal)) {
        return false;
      }
    }

    return true;
  }
}
