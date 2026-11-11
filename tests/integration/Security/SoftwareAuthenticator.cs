using System.Formats.Cbor;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;

namespace GameBot.IntegrationTests.Security;

/// <summary>
/// A software authenticator for tests (feature 187). It makes ES256 credentials with attestation <c>none</c>
/// and assertion responses, for a given challenge, origin, and RP ID.
/// </summary>
internal sealed class SoftwareAuthenticator : IDisposable {
  private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
  private byte[] _credentialId = RandomNumberGenerator.GetBytes(32);
  private uint _counter;

  /// <summary>The credential id as base64url text.</summary>
  public string CredentialId => WebEncoders.Base64UrlEncode(_credentialId);

  public void Dispose() => _key.Dispose();

  /// <summary>Makes a registration response for the creation options (the <c>publicKey</c> object of the service).</summary>
  public object CreateRegistration(JsonElement publicKey, string origin, string? challengeOverride = null) {
    var challenge = challengeOverride ?? publicKey.GetProperty("challenge").GetString()!;
    var rpId = publicKey.GetProperty("rp").GetProperty("id").GetString()!;
    var clientData = ClientData("webauthn.create", challenge, origin);
    var attestedData = AttestedCredentialData();
    var authData = AuthData(rpId, flags: 0x45, counter: 0, attestedData);
    var attestation = new CborWriter(CborConformanceMode.Ctap2Canonical);
    attestation.WriteStartMap(3);
    attestation.WriteTextString("fmt");
    attestation.WriteTextString("none");
    attestation.WriteTextString("attStmt");
    attestation.WriteStartMap(0);
    attestation.WriteEndMap();
    attestation.WriteTextString("authData");
    attestation.WriteByteString(authData);
    attestation.WriteEndMap();
    return new {
      id = CredentialId,
      rawId = CredentialId,
      type = "public-key",
      response = new {
        attestationObject = WebEncoders.Base64UrlEncode(attestation.Encode()),
        clientDataJSON = WebEncoders.Base64UrlEncode(clientData)
      }
    };
  }

  /// <summary>Makes an assertion response for the sign-in options.</summary>
  public object CreateAssertion(
    JsonElement publicKey, string origin, byte[] userHandle, string? challengeOverride = null, bool breakSignature = false) {
    var challenge = challengeOverride ?? publicKey.GetProperty("challenge").GetString()!;
    var rpId = publicKey.GetProperty("rpId").GetString()!;
    var clientData = ClientData("webauthn.get", challenge, origin);
    _counter++;
    var authData = AuthData(rpId, flags: 0x05, _counter, null);
    var signed = authData.Concat(SHA256.HashData(clientData)).ToArray();
    var signature = _key.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
    if (breakSignature) {
      signature[^1] ^= 0xFF;
    }

    return new {
      id = CredentialId,
      rawId = CredentialId,
      type = "public-key",
      response = new {
        authenticatorData = WebEncoders.Base64UrlEncode(authData),
        clientDataJSON = WebEncoders.Base64UrlEncode(clientData),
        signature = WebEncoders.Base64UrlEncode(signature),
        userHandle = WebEncoders.Base64UrlEncode(userHandle)
      }
    };
  }

  private static byte[] ClientData(string type, string challenge, string origin) =>
    Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { type, challenge, origin, crossOrigin = false }));

  private static byte[] AuthData(string rpId, byte flags, uint counter, byte[]? attested) {
    var data = new List<byte>();
    data.AddRange(SHA256.HashData(Encoding.UTF8.GetBytes(rpId)));
    data.Add(flags);
    data.AddRange(new[] { (byte)(counter >> 24), (byte)(counter >> 16), (byte)(counter >> 8), (byte)counter });
    if (attested is not null) {
      data.AddRange(attested);
    }

    return data.ToArray();
  }

  private byte[] AttestedCredentialData() {
    var data = new List<byte>();
    data.AddRange(new byte[16]);
    data.Add((byte)(_credentialId.Length >> 8));
    data.Add((byte)_credentialId.Length);
    data.AddRange(_credentialId);
    var p = _key.ExportParameters(false);
    var cose = new CborWriter(CborConformanceMode.Ctap2Canonical);
    cose.WriteStartMap(5);
    cose.WriteInt32(1);
    cose.WriteInt32(2);
    cose.WriteInt32(3);
    cose.WriteInt32(-7);
    cose.WriteInt32(-1);
    cose.WriteInt32(1);
    cose.WriteInt32(-2);
    cose.WriteByteString(p.Q.X!);
    cose.WriteInt32(-3);
    cose.WriteByteString(p.Q.Y!);
    cose.WriteEndMap();
    data.AddRange(cose.Encode());
    return data.ToArray();
  }
}
