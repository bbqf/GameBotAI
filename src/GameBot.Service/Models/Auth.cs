using System.Text.Json;

namespace GameBot.Service.Models;

/// <summary>The answer of GET /api/auth/status (contract C-1).</summary>
internal sealed class AuthStatusResponse {
  /// <summary>True when the sign-in function is on.</summary>
  public bool SignInEnabled { get; set; }

  /// <summary>True when the request has access by a session or by a valid access token, or when the sign-in function is off and the source passes.</summary>
  public bool SignedIn { get; set; }

  /// <summary>True when the request comes from a trusted proxy or has the host of the public origin.</summary>
  public bool PasskeysSupportedHere { get; set; }

  /// <summary>The source of the request: localMachine, homeNetwork, or internet.</summary>
  public string Source { get; set; } = "internet";

  /// <summary>The public origin. It has a value only when the source is localMachine or homeNetwork.</summary>
  public string? PublicOrigin { get; set; }

  /// <summary>True when an access token is configured and the source is localMachine or homeNetwork.</summary>
  public bool TokenAccessHere { get; set; }
}

/// <summary>The answer of the sign-in options call and of the registration options call (C-2, C-4).</summary>
internal sealed class AuthCeremonyResponse {
  /// <summary>The id that the complete call must send back.</summary>
  public string CeremonyId { get; set; } = string.Empty;

  /// <summary>The WebAuthn options for the browser, with binary values as base64url text.</summary>
  public JsonElement PublicKey { get; set; }
}

/// <summary>The request of the sign-in complete call (C-3).</summary>
internal sealed class SignInCompleteRequest {
  /// <summary>The id from the options call.</summary>
  public string? CeremonyId { get; set; }

  /// <summary>The credential from the browser.</summary>
  public JsonElement Credential { get; set; }
}

/// <summary>The request of the registration options call (C-4).</summary>
internal sealed class RegisterOptionsRequest {
  /// <summary>The registration code.</summary>
  public string? Code { get; set; }
}

/// <summary>The request of the registration complete call (C-5).</summary>
internal sealed class RegisterCompleteRequest {
  /// <summary>The id from the options call.</summary>
  public string? CeremonyId { get; set; }

  /// <summary>The optional device name, 1 to 40 characters.</summary>
  public string? Name { get; set; }

  /// <summary>The new credential from the browser.</summary>
  public JsonElement Credential { get; set; }
}

/// <summary>One registered passkey in the list of C-8. The item has no key material.</summary>
internal sealed class PasskeyItem {
  /// <summary>The short id.</summary>
  public string Id { get; set; } = string.Empty;

  /// <summary>The device name.</summary>
  public string Name { get; set; } = string.Empty;

  /// <summary>The registration time.</summary>
  public DateTimeOffset CreatedUtc { get; set; }

  /// <summary>The last sign-in time, or null.</summary>
  public DateTimeOffset? LastUsedUtc { get; set; }

  /// <summary>True for the passkey of this session.</summary>
  public bool Current { get; set; }
}

/// <summary>The answer of GET /api/auth/passkeys (C-8).</summary>
internal sealed class PasskeyListResponse {
  /// <summary>The registered passkeys.</summary>
  public IReadOnlyList<PasskeyItem> Passkeys { get; set; } = Array.Empty<PasskeyItem>();
}

/// <summary>The request of PATCH /api/auth/passkeys/{id} (C-9).</summary>
internal sealed class RenamePasskeyRequest {
  /// <summary>The new name, 1 to 40 characters.</summary>
  public string? Name { get; set; }
}

