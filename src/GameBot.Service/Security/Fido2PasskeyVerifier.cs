using System.Text.Json;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.AspNetCore.WebUtilities;

namespace GameBot.Service.Security;

/// <summary>
/// The passkey verifier on the Fido2 package (feature 187, research R1 and R3). All checks use the configured
/// public origin and not the request headers, so the flows work behind a proxy that changes the host name (FR-030).
/// </summary>
internal sealed class Fido2PasskeyVerifier : IPasskeyVerifier {
  private readonly Fido2? _fido2;

  public Fido2PasskeyVerifier(AuthOptions options) {
    ArgumentNullException.ThrowIfNull(options);
    if (options.PublicOrigin is null) {
      return;
    }

    _fido2 = new Fido2(new Fido2Configuration {
      RPID = options.RpId,
      RPName = "GameBot",
      Origins = new HashSet<string> { options.PublicOrigin.AbsoluteUri.TrimEnd('/') }
    });
  }

  public PasskeyOptions CreateRegistrationOptions(byte[] userHandle, IReadOnlyList<string> excludeCredentialIds) {
    ArgumentNullException.ThrowIfNull(userHandle);
    ArgumentNullException.ThrowIfNull(excludeCredentialIds);
    var fido2 = Require();
    var user = new Fido2User {
      Id = userHandle,
      Name = "owner",
      DisplayName = "GameBot owner"
    };
    var exclude = excludeCredentialIds
      .Select(id => new PublicKeyCredentialDescriptor(WebEncoders.Base64UrlDecode(id)))
      .ToList();
    var created = fido2.RequestNewCredential(new RequestNewCredentialParams {
      User = user,
      ExcludeCredentials = exclude,
      AuthenticatorSelection = new AuthenticatorSelection {
        ResidentKey = ResidentKeyRequirement.Required,
        UserVerification = UserVerificationRequirement.Required
      },
      AttestationPreference = AttestationConveyancePreference.None
    });
    return new PasskeyOptions {
      PublicKey = JsonSerializer.SerializeToElement(created),
      Native = created
    };
  }

  public PasskeyOptions CreateSignInOptions() {
    var fido2 = Require();
    var assertion = fido2.GetAssertionOptions(new GetAssertionOptionsParams {
      AllowedCredentials = new List<PublicKeyCredentialDescriptor>(),
      UserVerification = UserVerificationRequirement.Required
    });
    return new PasskeyOptions {
      PublicKey = JsonSerializer.SerializeToElement(assertion),
      Native = assertion
    };
  }

  public async Task<VerifiedRegistration?> VerifyRegistrationAsync(
    PasskeyOptions options, JsonElement credential, CancellationToken cancellationToken) {
    ArgumentNullException.ThrowIfNull(options);
    var fido2 = Require();
    try {
      var response = credential.Deserialize<AuthenticatorAttestationRawResponse>();
      if (response is null || options.Native is not CredentialCreateOptions original) {
        return null;
      }

      var registered = await fido2.MakeNewCredentialAsync(new MakeNewCredentialParams {
        AttestationResponse = response,
        OriginalOptions = original,
        IsCredentialIdUniqueToUserCallback = (_, _) => Task.FromResult(true)
      }, cancellationToken).ConfigureAwait(false);
      return new VerifiedRegistration(
        WebEncoders.Base64UrlEncode(registered.Id),
        WebEncoders.Base64UrlEncode(registered.PublicKey),
        registered.SignCount);
    }
    catch (Fido2VerificationException) {
      return null;
    }
    catch (JsonException) {
      return null;
    }
    catch (FormatException) {
      return null;
    }
  }

  public async Task<uint?> VerifySignInAsync(
    PasskeyOptions options,
    JsonElement credential,
    string storedPublicKey,
    uint storedSignCount,
    byte[] userHandle,
    CancellationToken cancellationToken) {
    ArgumentNullException.ThrowIfNull(options);
    ArgumentNullException.ThrowIfNull(userHandle);
    var fido2 = Require();
    try {
      var response = credential.Deserialize<AuthenticatorAssertionRawResponse>();
      if (response is null || options.Native is not AssertionOptions original) {
        return null;
      }

      var result = await fido2.MakeAssertionAsync(new MakeAssertionParams {
        AssertionResponse = response,
        OriginalOptions = original,
        StoredPublicKey = WebEncoders.Base64UrlDecode(storedPublicKey),
        StoredSignatureCounter = storedSignCount,
        IsUserHandleOwnerOfCredentialIdCallback = (args, _) => Task.FromResult(args.UserHandle.AsSpan().SequenceEqual(userHandle))
      }, cancellationToken).ConfigureAwait(false);
      return result.SignCount;
    }
    catch (Fido2VerificationException) {
      return null;
    }
    catch (JsonException) {
      return null;
    }
    catch (FormatException) {
      return null;
    }
  }

  private Fido2 Require() =>
    _fido2 ?? throw new InvalidOperationException("The public origin is not valid.");
}

