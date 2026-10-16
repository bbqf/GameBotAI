namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>The result of a signature check on one gamepack (feature 146).</summary>
public enum SignatureStatus {
  /// <summary>The signature is sound, the hashes match, and the key is trusted.</summary>
  Valid,

  /// <summary>No signature was sent.</summary>
  Missing,

  /// <summary>The signature is empty, has a bad format, or fails the crypto test.</summary>
  Invalid,

  /// <summary>The hashes of the gamepack differ from the signed list.</summary>
  ContentMismatch,

  /// <summary>The signature is sound, but the key is not on the trusted list.</summary>
  Untrusted,

  /// <summary>The policy is off. No check ran.</summary>
  NotChecked
}

/// <summary>The JSON text of <see cref="SignatureStatus"/>.</summary>
public static class SignatureStatusNames {
  /// <summary>Gives the snake_case JSON text of a status.</summary>
  /// <param name="status">The status.</param>
  public static string ToWire(this SignatureStatus status) => status switch {
    SignatureStatus.Valid => "valid",
    SignatureStatus.Missing => "missing",
    SignatureStatus.Invalid => "invalid",
    SignatureStatus.ContentMismatch => "content_mismatch",
    SignatureStatus.Untrusted => "untrusted",
    _ => "not_checked"
  };
}
