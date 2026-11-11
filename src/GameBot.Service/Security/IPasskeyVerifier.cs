using System.Text.Json;

namespace GameBot.Service.Security;

/// <summary>
/// Options for one passkey ceremony. <see cref="PublicKey"/> goes to the browser.
/// <see cref="Native"/> is private to the verifier and stays in the service.
/// </summary>
internal sealed class PasskeyOptions {
  public required JsonElement PublicKey { get; init; }

  public object? Native { get; init; }
}

/// <summary>A new credential that the verifier accepted.</summary>
internal sealed record VerifiedRegistration(string CredentialId, string PublicKey, uint SignCount);

/// <summary>
/// The WebAuthn checks (challenge, origin, RP ID, signature, sign count). Only <see cref="Fido2PasskeyVerifier"/>
/// uses the Fido2 package. Most tests use a fake of this interface (research R1).
/// </summary>
internal interface IPasskeyVerifier {
  /// <summary>Makes the creation options. <paramref name="excludeCredentialIds"/> holds the registered credential ids (base64url).</summary>
  PasskeyOptions CreateRegistrationOptions(byte[] userHandle, IReadOnlyList<string> excludeCredentialIds);

  /// <summary>Makes the sign-in options with no credential list.</summary>
  PasskeyOptions CreateSignInOptions();

  /// <summary>Checks a new credential. Gives null when the check fails.</summary>
  Task<VerifiedRegistration?> VerifyRegistrationAsync(PasskeyOptions options, JsonElement credential, CancellationToken cancellationToken);

  /// <summary>Checks a sign-in answer. Gives the new sign count, or null when the check fails.</summary>
  Task<uint?> VerifySignInAsync(
    PasskeyOptions options,
    JsonElement credential,
    string storedPublicKey,
    uint storedSignCount,
    byte[] userHandle,
    CancellationToken cancellationToken);
}
