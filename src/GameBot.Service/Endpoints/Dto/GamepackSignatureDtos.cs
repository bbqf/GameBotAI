using GameBot.Domain.Gamepacks.Signing;

namespace GameBot.Service.Endpoints.Dto;

/// <summary>The signature block of an install or dry-run response (feature 146).</summary>
internal sealed record GamepackSignatureStatusDto {
  /// <summary>valid, missing, invalid, content_mismatch, untrusted, or not_checked.</summary>
  public required string Status { get; init; }

  /// <summary>The policy at check time: required, optional, or off.</summary>
  public required string Policy { get; init; }

  /// <summary>True only for the policy required with a status other than valid.</summary>
  public bool WouldReject { get; init; }

  internal static GamepackSignatureStatusDto From(SignatureCheckResult result) {
    ArgumentNullException.ThrowIfNull(result);
    return new GamepackSignatureStatusDto { Status = result.Status.ToWire(), Policy = result.Policy.ToWire(), WouldReject = result.WouldReject };
  }
}

/// <summary>The signature policy of the instance.</summary>
internal sealed record GamepackSignaturePolicyDto {
  /// <summary>required, optional, or off.</summary>
  public string? Policy { get; init; }
}

/// <summary>One trusted key in a list or in a create response. The public key text is not shown.</summary>
internal sealed record GamepackSignatureKeyDto {
  /// <summary>The display label.</summary>
  public required string Label { get; init; }

  /// <summary>The SHA-256 of the SPKI bytes, in lower case hex.</summary>
  public required string Fingerprint { get; init; }

  internal static GamepackSignatureKeyDto From(TrustedKey key) {
    ArgumentNullException.ThrowIfNull(key);
    return new GamepackSignatureKeyDto { Label = key.Label, Fingerprint = key.Fingerprint };
  }
}

/// <summary>The request to add a trusted key.</summary>
internal sealed record GamepackSignatureKeyRequest {
  /// <summary>The display label, 1 to 100 characters.</summary>
  public string? Label { get; init; }

  /// <summary>The ECDSA P-256 public key as base64 SubjectPublicKeyInfo.</summary>
  public string? PublicKey { get; init; }
}
