using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>Feature 187: describes the types of the sign-in API in simple technical English. The service gives no XML comments to Swagger.</summary>
internal sealed class AuthSchemaFilter : ISchemaFilter {
  private static readonly Dictionary<Type, (string Description, Dictionary<string, string> Fields)> Types = new() {
    [typeof(AuthStatusResponse)] = ("The sign-in state of one request. It does not show if a passkey or a code exists.", new() {
      ["signInEnabled"] = "True when the sign-in function is on.",
      ["signedIn"] = "True when the request has access by a session or a valid access token, or when the sign-in function is off and the request passes.",
      ["passkeysSupportedHere"] = "True when the request comes from a trusted proxy or has the host of the public origin.",
      ["source"] = "Where the request comes from: localMachine, homeNetwork, or internet.",
      ["publicOrigin"] = "The public https address. It has a value only when the source is localMachine or homeNetwork.",
      ["tokenAccessHere"] = "True when an access token is set and the source is localMachine or homeNetwork."
    }),
    [typeof(AuthCeremonyResponse)] = ("A passkey challenge for the browser.", new() {
      ["ceremonyId"] = "The id that the complete call must send back. It is valid for 5 minutes.",
      ["publicKey"] = "The WebAuthn options. Binary values are base64url text."
    }),
    [typeof(SignInCompleteRequest)] = ("The answer of the browser to a sign-in challenge.", new() {
      ["ceremonyId"] = "The id from the options call.",
      ["credential"] = "The credential from navigator.credentials.get, with binary values as base64url text."
    }),
    [typeof(RegisterOptionsRequest)] = ("The request to start the registration of a passkey.", new() {
      ["code"] = "The one-time registration code. The service ignores case, spaces, and the hyphen."
    }),
    [typeof(RegisterCompleteRequest)] = ("The answer of the browser to a registration challenge.", new() {
      ["ceremonyId"] = "The id from the options call.",
      ["name"] = "The device name, 1 to 40 characters. A name that is not valid gives the default name.",
      ["credential"] = "The credential from navigator.credentials.create, with binary values as base64url text."
    }),
    [typeof(PasskeyItem)] = ("One registered passkey. It has no key material.", new() {
      ["id"] = "The short id of the passkey.",
      ["name"] = "The device name.",
      ["createdUtc"] = "The time of the registration.",
      ["lastUsedUtc"] = "The time of the last sign-in, or null.",
      ["current"] = "True for the passkey of this session."
    }),
    [typeof(PasskeyListResponse)] = ("The registered passkeys.", new() {
      ["passkeys"] = "The registered passkeys."
    }),
    [typeof(RenamePasskeyRequest)] = ("The request to rename a passkey.", new() {
      ["name"] = "The new name, 1 to 40 characters."
    })
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    ArgumentNullException.ThrowIfNull(schema);
    ArgumentNullException.ThrowIfNull(context);
    if (!Types.TryGetValue(context.Type, out var entry)) {
      return;
    }

    schema.Description = entry.Description;
    foreach (var (name, description) in entry.Fields) {
      if (schema.Properties.TryGetValue(name, out var property)) {
        property.Description = description;
      }
    }
  }
}
