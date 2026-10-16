using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using GameBot.Domain.Gamepacks.Signing;

#pragma warning disable CA2007, CA1861, CA1707, CA1002, CA1819, CA2227, CA2000, CA1305, CA1307, CA1310

namespace GameBot.UnitTests.Gamepacks.Signing;

/// <summary>
/// A test signer (feature 146). It holds one P-256 key and signs a gamepack file text with the documented format. The helper
/// is also the proof that the format in the documentation is enough to build a signature.
/// </summary>
internal sealed class TestSigner : IDisposable {
  private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

  /// <summary>The base64 SPKI text of the public key.</summary>
  public string PublicKey => Convert.ToBase64String(_key.ExportSubjectPublicKeyInfo());

  /// <summary>The fingerprint of the public key.</summary>
  public string Fingerprint => Convert.ToHexStringLower(SHA256.HashData(_key.ExportSubjectPublicKeyInfo()));

  public TrustedKey AsTrusted(string label = "Builder") => new() { Label = label, Fingerprint = Fingerprint, PublicKey = PublicKey };

  /// <summary>Makes the signature file text for a gamepack file text.</summary>
  public string Sign(string gamepackJson, Action<GamepackHashes, System.Collections.Generic.List<SignedFileEntry>>? tamper = null, bool corruptSignature = false) {
    var hashes = GamepackContentHasher.Compute(gamepackJson)!;
    var files = hashes.Files.ToList();
    tamper?.Invoke(hashes, files);
    var bytes = SignatureFile.BuildSignedBytes(hashes.ManifestSha256, files);
    var signature = _key.SignData(bytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    if (corruptSignature) {
      signature[0] ^= 0xFF;
    }

    return Write(hashes.ManifestSha256, files, signature);
  }

  /// <summary>Makes a signature file text with a changed byte in the signature.</summary>
  public string SignWithBadSignature(string gamepackJson) => Sign(gamepackJson, corruptSignature: true);

  private string Write(string manifestSha256, System.Collections.Generic.IEnumerable<SignedFileEntry> files, byte[] signature) =>
    JsonSerializer.Serialize(new {
      format = SignatureFile.FormatName,
      algorithm = SignatureFile.AlgorithmName,
      publicKey = PublicKey,
      manifestSha256,
      files = files.Select(f => new { type = f.Type, name = f.Name, sha256 = f.Sha256 }).ToArray(),
      signature = Convert.ToBase64String(signature)
    });

  public void Dispose() => _key.Dispose();
}
