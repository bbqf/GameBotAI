namespace GameBot.Service.Security;

/// <summary>
/// The access gate (feature 187, plan Design 1). It replaces the old token block in <c>Program.cs</c>.
/// It follows the rule table of contracts/auth-api.md and fails closed.
/// </summary>
internal sealed class AccessGateMiddleware {
  /// <summary>The key of the valid session in <see cref="HttpContext.Items"/>.</summary>
  internal const string SessionItemKey = "GameBot.Auth.Session";

  /// <summary>The key that is true in <see cref="HttpContext.Items"/> when the request has a valid access token.</summary>
  internal const string TokenItemKey = "GameBot.Auth.Token";

  private readonly RequestDelegate _next;
  private readonly AuthOptions _options;
  private readonly RequestOriginClassifier _classifier;
  private readonly ClientAddressResolver _resolver;
  private readonly ISessionValidator _sessions;
  private readonly FailedAttemptLimiter _limiter;
  private readonly SignInRecorder _recorder;

  public AccessGateMiddleware(
    RequestDelegate next,
    AuthOptions options,
    RequestOriginClassifier classifier,
    ClientAddressResolver resolver,
    ISessionValidator sessions,
    FailedAttemptLimiter limiter,
    SignInRecorder recorder) {
    _next = next ?? throw new ArgumentNullException(nameof(next));
    _options = options ?? throw new ArgumentNullException(nameof(options));
    _classifier = classifier ?? throw new ArgumentNullException(nameof(classifier));
    _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
    _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    _limiter = limiter ?? throw new ArgumentNullException(nameof(limiter));
    _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
  }

  public async Task InvokeAsync(HttpContext context) {
    ArgumentNullException.ThrowIfNull(context);
    var path = context.Request.Path.Value ?? string.Empty;
    var method = context.Request.Method;

    // Step 1: preflight and health always pass.
    if (HttpMethods.IsOptions(method)
        || path.Equals("/health", StringComparison.OrdinalIgnoreCase)
        || path.StartsWith("/health/", StringComparison.OrdinalIgnoreCase)) {
      await _next(context).ConfigureAwait(false);
      return;
    }

    var origin = _classifier.Classify(context);
    if (!_options.Enabled) {
      await HandleOffModeAsync(context, origin, path).ConfigureAwait(false);
      return;
    }

    await HandleOnModeAsync(context, origin, path, method).ConfigureAwait(false);
  }

  private async Task HandleOffModeAsync(HttpContext context, RequestOrigin origin, string path) {
    if (origin == RequestOrigin.Internet) {
      _recorder.RecordRemoteAccessRefused(_resolver.Resolve(context));
      await AuthRefusals.WriteRemoteRefusedAsync(context.Response).ConfigureAwait(false);
      return;
    }

    // With the sign-in function off, a request from the service machine or the home network behaves as before
    // this feature: the Swagger pages need no token. The internet cannot reach them (refusal above).
    if (string.IsNullOrWhiteSpace(_options.Token)
        || path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase)) {
      await _next(context).ConfigureAwait(false);
      return;
    }

    await TokenAuthMiddleware.Invoke(context, _next, _options.Token).ConfigureAwait(false);
  }

  private async Task HandleOnModeAsync(HttpContext context, RequestOrigin origin, string path, string method) {
    // The web UI shell is open. It has no application data (research R9).
    if (IsOpenShellRequest(path, method)) {
      await _next(context).ConfigureAwait(false);
      return;
    }

    var tokenOk = CheckToken(context, origin);
    var session = _sessions.Validate(context);
    if (tokenOk) {
      context.Items[TokenItemKey] = true;
    }

    if (session is not null) {
      context.Items[SessionItemKey] = session;
    }

    if (IsOpenAuthEndpoint(path)) {
      await _next(context).ConfigureAwait(false);
      return;
    }

    if (tokenOk) {
      await _next(context).ConfigureAwait(false);
      return;
    }

    if (session is not null && PassesOriginRule(context, method)) {
      if (session.RefreshCookie && context.Request.Cookies.TryGetValue(SessionStore.CookieName, out var cookie)) {
        context.Response.OnStarting(() => {
          SessionStore.WriteCookie(context.Response, cookie);
          return Task.CompletedTask;
        });
      }

      await _next(context).ConfigureAwait(false);
      return;
    }

    await AuthRefusals.WriteSignInNecessaryAsync(context.Response).ConfigureAwait(false);
  }

  // A bearer token counts only for a request from the service machine or the home network.
  // A wrong token counts for the limiter. A request with no token is not a failed attempt.
  private bool CheckToken(HttpContext context, RequestOrigin origin) {
    if (!TokenAuthMiddleware.TryGetBearer(context, out var provided)) {
      return false;
    }

    var source = _resolver.Resolve(context);
    if (_limiter.IsBlocked(source)) {
      return false;
    }

    var matches = !string.IsNullOrWhiteSpace(_options.Token) && TokenAuthMiddleware.Matches(provided, _options.Token);
    if (!matches) {
      if (_limiter.RegisterFailure(source)) {
        _recorder.Record(SignInEvents.SourceBlocked, source, null, "wrong_token");
      }

      return false;
    }

    return origin != RequestOrigin.Internet;
  }

  private bool PassesOriginRule(HttpContext context, string method) {
    if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method)) {
      return true;
    }

    var header = context.Request.Headers.Origin.ToString();
    if (string.IsNullOrEmpty(header)) {
      return true;
    }

    var publicOrigin = _options.PublicOrigin;
    return publicOrigin is not null
           && string.Equals(header.TrimEnd('/'), publicOrigin.AbsoluteUri.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);
  }

  private static bool IsOpenShellRequest(string path, string method) =>
    (HttpMethods.IsGet(method) || HttpMethods.IsHead(method))
    && !path.StartsWith("/api", StringComparison.OrdinalIgnoreCase)
    && !path.StartsWith("/swagger", StringComparison.OrdinalIgnoreCase);

  private static bool IsOpenAuthEndpoint(string path) =>
    path.Equals(ApiRoutes.AuthStatus, StringComparison.OrdinalIgnoreCase)
    || path.Equals(ApiRoutes.AuthSignInOptions, StringComparison.OrdinalIgnoreCase)
    || path.Equals(ApiRoutes.AuthSignInComplete, StringComparison.OrdinalIgnoreCase)
    || path.Equals(ApiRoutes.AuthRegisterOptions, StringComparison.OrdinalIgnoreCase)
    || path.Equals(ApiRoutes.AuthRegisterComplete, StringComparison.OrdinalIgnoreCase);
}
