using System.Security.Cryptography;
using System.Text;

namespace GameBot.Service.Security;

/// <summary>The content of <c>registration-code.json</c>. The file exists only while a code is active.</summary>
internal sealed class RegistrationCodeFile {
  public string? CodeHash { get; set; }

  public DateTimeOffset ExpiresUtc { get; set; }

  public int FailedAttempts { get; set; }
}

/// <summary>The result of a code check.</summary>
internal enum CodeCheckResult {
  Valid,
  Invalid,
  Expired,
  NoCode
}

/// <summary>
/// The one-time registration code (feature 187, research R7). Only the hash is in the file.
/// Only the local command makes a code. The service only checks and uses it.
/// </summary>
internal sealed class RegistrationCodeStore {
  internal const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
  internal const int CodeLength = 8;
  internal const int MaxFailedAttempts = 5;
  internal static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

  private readonly AuthFileStore<RegistrationCodeFile> _store;
  private readonly TimeProvider _time;

  public RegistrationCodeStore(string authDirectory, TimeProvider time) {
    ArgumentNullException.ThrowIfNull(authDirectory);
    _time = time ?? throw new ArgumentNullException(nameof(time));
    _store = new AuthFileStore<RegistrationCodeFile>(
      Path.Combine(authDirectory, "registration-code.json"), time, file => file.CodeHash is null);
  }

  /// <summary>Makes a new code and cancels an older code. Gives the code as <c>XXXX-XXXX</c>.</summary>
  public string NewCode() {
    var chars = new char[CodeLength];
    for (var i = 0; i < chars.Length; i++) {
      chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
    }

    var plain = new string(chars);
    _store.Update(file => {
      file.CodeHash = Hash(plain);
      file.ExpiresUtc = _time.GetUtcNow() + Lifetime;
      file.FailedAttempts = 0;
    });
    return string.Concat(plain.AsSpan(0, 4), "-", plain.AsSpan(4));
  }

  /// <summary>Checks a code. A wrong code counts, and the 5th wrong code deletes the active code.</summary>
  public CodeCheckResult Check(string? code, out string? codeHash) {
    codeHash = null;
    string? matched = null;
    var result = _store.Update(file => {
      if (file.CodeHash is null) {
        return CodeCheckResult.NoCode;
      }

      if (file.ExpiresUtc <= _time.GetUtcNow()) {
        file.CodeHash = null;
        return CodeCheckResult.Expired;
      }

      if (FixedTimeEquals(Hash(Normalize(code)), file.CodeHash)) {
        matched = file.CodeHash;
        return CodeCheckResult.Valid;
      }

      file.FailedAttempts++;
      if (file.FailedAttempts >= MaxFailedAttempts) {
        file.CodeHash = null;
      }

      return CodeCheckResult.Invalid;
    });
    codeHash = matched;
    return result;
  }

  /// <summary>Uses the code. Returns false when the active code is not the one with this hash any more.</summary>
  public bool Consume(string codeHash) =>
    _store.Update(file => {
      if (file.CodeHash is null
          || file.ExpiresUtc <= _time.GetUtcNow()
          || !FixedTimeEquals(codeHash, file.CodeHash)) {
        return false;
      }

      file.CodeHash = null;
      return true;
    });

  /// <summary>True when a code is active now.</summary>
  public bool HasActiveCode {
    get {
      var file = _store.Snapshot();
      return file.CodeHash is not null && file.ExpiresUtc > _time.GetUtcNow();
    }
  }

  /// <summary>Removes spaces and hyphens and changes the text to upper case.</summary>
  internal static string Normalize(string? code) =>
    new string((code ?? string.Empty).Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();

  private static string Hash(string normalCode) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalCode))).ToLowerInvariant();

  private static bool FixedTimeEquals(string a, string b) =>
    CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
