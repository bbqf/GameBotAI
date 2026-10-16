namespace GameBot.Domain.Gamepacks.Signing;

/// <summary>The signature policy of the instance (feature 146).</summary>
public enum SignaturePolicy {
  /// <summary>No check. No signature is read. This is the default.</summary>
  Off = 0,

  /// <summary>Every gamepack installs. The result shows the status.</summary>
  Optional = 1,

  /// <summary>A gamepack with a status other than valid is rejected.</summary>
  Required = 2
}

/// <summary>The JSON text of <see cref="SignaturePolicy"/>.</summary>
public static class SignaturePolicyNames {
  /// <summary>Gives the JSON text of a policy: required, optional, or off.</summary>
  /// <param name="policy">The policy.</param>
  public static string ToWire(this SignaturePolicy policy) => policy switch {
    SignaturePolicy.Required => "required",
    SignaturePolicy.Optional => "optional",
    _ => "off"
  };

  /// <summary>Reads the JSON text of a policy. The case is ignored.</summary>
  /// <param name="text">The text.</param>
  /// <param name="policy">The policy, when the text is known.</param>
  public static bool TryParse(string? text, out SignaturePolicy policy) {
    var value = text?.Trim();
    foreach (var candidate in new[] { SignaturePolicy.Required, SignaturePolicy.Optional, SignaturePolicy.Off }) {
      if (string.Equals(value, candidate.ToWire(), StringComparison.OrdinalIgnoreCase)) {
        policy = candidate;
        return true;
      }
    }

    policy = SignaturePolicy.Off;
    return false;
  }
}
