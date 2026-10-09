using System.Security.Cryptography;

namespace GameBot.Domain.Updates;

/// <summary>Computes a SHA-256 checksum and compares it with an expected hex string (FR-004).</summary>
public static class ChecksumVerifier {
  public const int Sha256HexLength = 64;

  /// <summary>True when the text has exactly 64 hexadecimal characters.</summary>
  public static bool IsValidSha256Hex(string? value) {
    if (value is null || value.Length != Sha256HexLength) {
      return false;
    }

    foreach (var c in value) {
      if (!char.IsAsciiHexDigit(c)) {
        return false;
      }
    }

    return true;
  }

  /// <summary>Compute the SHA-256 of a stream. The result is lower-case hex.</summary>
  public static async Task<string> ComputeSha256HexAsync(Stream stream, CancellationToken ct = default) {
    ArgumentNullException.ThrowIfNull(stream);
    var hash = await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false);
    return Convert.ToHexStringLower(hash);
  }

  /// <summary>Compare the SHA-256 of a stream with a 64-character hex string. The compare ignores case.</summary>
  public static async Task<bool> MatchesAsync(Stream stream, string expectedHex, CancellationToken ct = default) {
    if (!IsValidSha256Hex(expectedHex)) {
      return false;
    }

    var actual = await ComputeSha256HexAsync(stream, ct).ConfigureAwait(false);
    return string.Equals(actual, expectedHex, StringComparison.OrdinalIgnoreCase);
  }
}
