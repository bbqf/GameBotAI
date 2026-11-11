using System.Text.Json;
using GameBot.Service.Models;
using GameBot.Service.Security;
using Microsoft.AspNetCore.WebUtilities;

namespace GameBot.Service.Endpoints;

/// <summary>The auth endpoints C-1 to C-10 of feature 187 (contracts/auth-api.md). The handlers are named methods.</summary>
internal static class AuthEndpoints {
  public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app) {
    app.MapGet(ApiRoutes.AuthStatus, GetStatus)
      .Produces<AuthStatusResponse>(StatusCodes.Status200OK)
      .WithName("GetAuthStatus").WithTags("Auth")
      .WithSummary("Read the sign-in state of this request")
      .WithDescription("Open call. Tells if the sign-in function is on, if this request has access, where it comes from, and if passkeys work at this address.");
    app.MapPost(ApiRoutes.AuthSignInOptions, SignInOptions)
      .Produces<AuthCeremonyResponse>(StatusCodes.Status200OK)
      .WithName("AuthSignInOptions").WithTags("Auth")
      .WithSummary("Start a passkey sign-in")
      .WithDescription("Open call. Gives a challenge. The answer is the same when no passkey is registered.");
    app.MapPost(ApiRoutes.AuthSignInComplete, SignInComplete)
      .Accepts<SignInCompleteRequest>("application/json")
      .WithName("AuthSignInComplete").WithTags("Auth")
      .WithSummary("Finish a passkey sign-in")
      .WithDescription("Open call. Gives 204 and the session cookie, or a fixed refusal.");
    app.MapPost(ApiRoutes.AuthRegisterOptions, RegisterOptions)
      .Accepts<RegisterOptionsRequest>("application/json")
      .Produces<AuthCeremonyResponse>(StatusCodes.Status200OK)
      .WithName("AuthRegisterOptions").WithTags("Auth")
      .WithSummary("Start the registration of a passkey")
      .WithDescription("Open call. Needs the one-time registration code that the local command makes.");
    app.MapPost(ApiRoutes.AuthRegisterComplete, RegisterComplete)
      .Accepts<RegisterCompleteRequest>("application/json")
      .WithName("AuthRegisterComplete").WithTags("Auth")
      .WithSummary("Finish the registration of a passkey")
      .WithDescription("Open call. Needs the ceremony of the options call. Gives 204 and the session cookie, or a fixed refusal.");
    app.MapPost(ApiRoutes.AuthSignOut, SignOut)
      .WithName("AuthSignOut").WithTags("Auth")
      .WithSummary("End this session");
    app.MapPost(ApiRoutes.AuthSessionsEndAll, EndAllSessions)
      .WithName("AuthEndAllSessions").WithTags("Auth")
      .WithSummary("End all sessions");
    app.MapGet(ApiRoutes.AuthPasskeys, ListPasskeys)
      .Produces<PasskeyListResponse>(StatusCodes.Status200OK)
      .WithName("AuthListPasskeys").WithTags("Auth")
      .WithSummary("List the registered passkeys");
    app.MapPatch(ApiRoutes.AuthPasskeys + "/{id}", RenamePasskey)
      .Accepts<RenamePasskeyRequest>("application/json")
      .Produces<PasskeyItem>(StatusCodes.Status200OK)
      .WithName("AuthRenamePasskey").WithTags("Auth")
      .WithSummary("Rename a passkey");
    app.MapDelete(ApiRoutes.AuthPasskeys + "/{id}", RemovePasskey)
      .WithName("AuthRemovePasskey").WithTags("Auth")
      .WithSummary("Remove a passkey and end its sessions");
    return app;
  }

  private static IResult GetStatus(
    HttpContext context, AuthOptions options, RequestOriginClassifier classifier) {
    var origin = classifier.Classify(context);
    var inside = origin != RequestOrigin.Internet;
    var host = context.Request.Host.Host;
    var publicOrigin = options.PublicOrigin;
    var supported = publicOrigin is not null
                    && (options.IsTrustedProxy(context.Connection.RemoteIpAddress)
                        || string.Equals(host, publicOrigin.Host, StringComparison.OrdinalIgnoreCase));
    var signedIn = !options.Enabled
                   || context.Items.ContainsKey(AccessGateMiddleware.SessionItemKey)
                   || context.Items.ContainsKey(AccessGateMiddleware.TokenItemKey);
    return Results.Ok(new AuthStatusResponse {
      SignInEnabled = options.Enabled,
      SignedIn = signedIn,
      PasskeysSupportedHere = supported,
      Source = origin switch {
        RequestOrigin.LocalMachine => "localMachine",
        RequestOrigin.HomeNetwork => "homeNetwork",
        _ => "internet"
      },
      PublicOrigin = inside ? publicOrigin?.AbsoluteUri.TrimEnd('/') : null,
      TokenAccessHere = inside && !string.IsNullOrWhiteSpace(options.Token)
    });
  }

  private static IResult SignInOptions(
    HttpContext context,
    AuthOptions options,
    ClientAddressResolver resolver,
    FailedAttemptLimiter limiter,
    IPasskeyVerifier verifier,
    PendingCeremonyCache cache) {
    var source = resolver.Resolve(context);
    if (!options.IsValid || limiter.IsBlocked(source)) {
      return AuthRefusals.SignInNecessary();
    }

    var made = verifier.CreateSignInOptions();
    var id = cache.Add(CeremonyKind.SignIn, source, made);
    return Results.Ok(new AuthCeremonyResponse { CeremonyId = id, PublicKey = made.PublicKey });
  }

  private static async Task<IResult> SignInComplete(
    HttpContext context,
    AuthOptions options,
    ClientAddressResolver resolver,
    FailedAttemptLimiter limiter,
    SignInRecorder recorder,
    IPasskeyVerifier verifier,
    PendingCeremonyCache cache,
    PasskeyStore passkeys,
    SessionStore sessions) {
    var source = resolver.Resolve(context);
    if (!options.IsValid || limiter.IsBlocked(source)) {
      return AuthRefusals.SignInNecessary();
    }

    var request = await ReadBodyAsync<SignInCompleteRequest>(context).ConfigureAwait(false);
    var ceremony = cache.Take(request?.CeremonyId, CeremonyKind.SignIn);
    if (request is null || ceremony is null) {
      return Fail(SignInEvents.SignInFailed, "ceremony_unknown", source, limiter, recorder);
    }

    var credentialId = ReadCredentialId(request.Credential);
    var passkey = credentialId is null ? null : passkeys.FindByCredentialId(credentialId);
    var handle = passkeys.UserHandle();
    if (passkey is null || handle is null) {
      return Fail(SignInEvents.SignInFailed, "passkey_not_registered", source, limiter, recorder);
    }

    var newCount = await verifier.VerifySignInAsync(
      ceremony.Options, request.Credential, passkey.PublicKey, passkey.SignCount, handle, context.RequestAborted).ConfigureAwait(false);
    if (newCount is null) {
      return Fail(SignInEvents.SignInFailed, "verification_failed", source, limiter, recorder, passkey.Id);
    }

    passkeys.RecordSignIn(passkey.Id, newCount.Value);
    SessionStore.WriteCookie(context.Response, sessions.Create(passkey.Id));
    recorder.Record(SignInEvents.SignIn, source, passkey.Id);
    return Results.NoContent();
  }

  private static async Task<IResult> RegisterOptions(
    HttpContext context,
    AuthOptions options,
    ClientAddressResolver resolver,
    FailedAttemptLimiter limiter,
    SignInRecorder recorder,
    IPasskeyVerifier verifier,
    PendingCeremonyCache cache,
    PasskeyStore passkeys,
    RegistrationCodeStore codes) {
    var source = resolver.Resolve(context);
    if (!options.IsValid || limiter.IsBlocked(source)) {
      return AuthRefusals.SignInNecessary();
    }

    var request = await ReadBodyAsync<RegisterOptionsRequest>(context).ConfigureAwait(false);
    var check = codes.Check(request?.Code, out var codeHash);
    if (check != CodeCheckResult.Valid || codeHash is null) {
      return Fail(SignInEvents.RegistrationFailed, "code_" + check.ToString().ToLowerInvariant(), source, limiter, recorder);
    }

    if (passkeys.Count >= PasskeyStore.MaxPasskeys) {
      return Fail(SignInEvents.RegistrationFailed, "passkey_limit_reached", source, limiter, recorder);
    }

    var made = verifier.CreateRegistrationOptions(
      passkeys.GetOrCreateUserHandle(), passkeys.List().Select(p => p.CredentialId).ToList());
    var id = cache.Add(CeremonyKind.Registration, source, made, codeHash);
    return Results.Ok(new AuthCeremonyResponse { CeremonyId = id, PublicKey = made.PublicKey });
  }

  private static async Task<IResult> RegisterComplete(
    HttpContext context,
    AuthOptions options,
    ClientAddressResolver resolver,
    FailedAttemptLimiter limiter,
    SignInRecorder recorder,
    IPasskeyVerifier verifier,
    PendingCeremonyCache cache,
    PasskeyStore passkeys,
    RegistrationCodeStore codes,
    SessionStore sessions) {
    var source = resolver.Resolve(context);
    if (!options.IsValid || limiter.IsBlocked(source)) {
      return AuthRefusals.SignInNecessary();
    }

    var request = await ReadBodyAsync<RegisterCompleteRequest>(context).ConfigureAwait(false);
    var ceremony = cache.Take(request?.CeremonyId, CeremonyKind.Registration);
    if (request is null || ceremony?.CodeHash is null) {
      return Fail(SignInEvents.RegistrationFailed, "ceremony_unknown", source, limiter, recorder);
    }

    var verified = await verifier.VerifyRegistrationAsync(ceremony.Options, request.Credential, context.RequestAborted).ConfigureAwait(false);
    if (verified is null) {
      return Fail(SignInEvents.RegistrationFailed, "verification_failed", source, limiter, recorder);
    }

    var added = passkeys.Add(verified.CredentialId, verified.PublicKey, verified.SignCount, request.Name, out var passkey);
    if (added != PasskeyAddResult.Added || passkey is null) {
      return Fail(SignInEvents.RegistrationFailed, added == PasskeyAddResult.Duplicate ? "credential_duplicate" : "passkey_limit_reached", source, limiter, recorder);
    }

    // The code is used only after the passkey is saved. If the code is gone now, the new passkey is removed again.
    if (!codes.Consume(ceremony.CodeHash)) {
      passkeys.Remove(passkey.Id);
      return Fail(SignInEvents.RegistrationFailed, "code_no_longer_active", source, limiter, recorder);
    }

    SessionStore.WriteCookie(context.Response, sessions.Create(passkey.Id));
    recorder.Record(SignInEvents.PasskeyRegistered, source, passkey.Id);
    return Results.NoContent();
  }

  private static IResult SignOut(
    HttpContext context, ClientAddressResolver resolver, SignInRecorder recorder, SessionStore sessions) {
    if (context.Items[AccessGateMiddleware.SessionItemKey] is SessionInfo session) {
      sessions.End(session.TokenHash);
      recorder.Record(SignInEvents.SignOut, resolver.Resolve(context), session.PasskeyId);
    }

    SessionStore.ClearCookie(context.Response);
    return Results.NoContent();
  }

  private static IResult EndAllSessions(
    HttpContext context, ClientAddressResolver resolver, SignInRecorder recorder, SessionStore sessions) {
    sessions.EndAll();
    recorder.Record(SignInEvents.SessionsEnded, resolver.Resolve(context), null, "all");
    SessionStore.ClearCookie(context.Response);
    return Results.NoContent();
  }

  private static IResult ListPasskeys(HttpContext context, PasskeyStore passkeys) {
    var current = (context.Items[AccessGateMiddleware.SessionItemKey] as SessionInfo)?.PasskeyId;
    return Results.Ok(new PasskeyListResponse {
      Passkeys = passkeys.List().Select(p => ToItem(p, current)).ToList()
    });
  }

  private static async Task<IResult> RenamePasskey(HttpContext context, string id, PasskeyStore passkeys) {
    var request = await ReadBodyAsync<RenamePasskeyRequest>(context).ConfigureAwait(false);
    if (PasskeyStore.NormalizeName(request?.Name) is null) {
      return Error(StatusCodes.Status400BadRequest, "invalid_name", "The name must have 1 to 40 characters.");
    }

    var changed = passkeys.Rename(id, request!.Name!);
    if (changed is null) {
      return Error(StatusCodes.Status404NotFound, "not_found", "The passkey does not exist.");
    }

    var current = (context.Items[AccessGateMiddleware.SessionItemKey] as SessionInfo)?.PasskeyId;
    return Results.Ok(ToItem(changed, current));
  }

  private static IResult RemovePasskey(
    HttpContext context, string id, ClientAddressResolver resolver, SignInRecorder recorder, PasskeyStore passkeys, SessionStore sessions) {
    if (!passkeys.Remove(id)) {
      return Error(StatusCodes.Status404NotFound, "not_found", "The passkey does not exist.");
    }

    sessions.EndForPasskey(id);
    if (context.Items[AccessGateMiddleware.SessionItemKey] is SessionInfo session
        && string.Equals(session.PasskeyId, id, StringComparison.Ordinal)) {
      SessionStore.ClearCookie(context.Response);
    }

    recorder.Record(SignInEvents.PasskeyRemoved, resolver.Resolve(context), id);
    return Results.NoContent();
  }

  private static PasskeyItem ToItem(PasskeyRecord p, string? currentId) => new() {
    Id = p.Id,
    Name = p.Name,
    CreatedUtc = p.CreatedUtc,
    LastUsedUtc = p.LastUsedUtc,
    Current = string.Equals(p.Id, currentId, StringComparison.Ordinal)
  };

  private static IResult Error(int status, string code, string message) =>
    Results.Json(new { error = new { code, message, hint = (string?)null } }, statusCode: status);

  // One failure path for sign-in and registration. The cause goes only to the record. The body is always A-1.
  private static IResult Fail(
    string eventName, string detail, string source, FailedAttemptLimiter limiter, SignInRecorder recorder, string? passkeyId = null) {
    recorder.Record(eventName, source, passkeyId, detail);
    if (limiter.RegisterFailure(source)) {
      recorder.Record(SignInEvents.SourceBlocked, source, null, "too_many_failures");
    }

    return AuthRefusals.SignInNecessary();
  }

  private static async Task<T?> ReadBodyAsync<T>(HttpContext context) where T : class {
    try {
      return await context.Request.ReadFromJsonAsync<T>(context.RequestAborted).ConfigureAwait(false);
    }
    catch (JsonException) {
      return null;
    }
    catch (InvalidOperationException) {
      return null;
    }
    catch (BadHttpRequestException) {
      return null;
    }
  }

  private static string? ReadCredentialId(JsonElement credential) {
    if (credential.ValueKind != JsonValueKind.Object) {
      return null;
    }

    foreach (var name in new[] { "rawId", "id" }) {
      if (credential.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) {
        var text = value.GetString();
        if (!string.IsNullOrEmpty(text)) {
          return NormalizeBase64Url(text);
        }
      }
    }

    return null;
  }

  private static string? NormalizeBase64Url(string text) {
    try {
      return WebEncoders.Base64UrlEncode(WebEncoders.Base64UrlDecode(text));
    }
    catch (FormatException) {
      return null;
    }
  }
}
