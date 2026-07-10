namespace GameBot.Service.Endpoints;

// Legacy guard rails: respond with guidance instead of serving old roots
internal static class LegacyGuardEndpoints {
  private static readonly (string LegacyRoot, string CanonicalRoot)[] LegacyRoots = {
    ("/commands", ApiRoutes.Commands),
    ("/triggers", ApiRoutes.Triggers),
    ("/games", ApiRoutes.Games),
    ("/sessions", ApiRoutes.Sessions),
    ("/images", ApiRoutes.Images),
    ("/images/detect", ApiRoutes.ImageDetect),
    ("/metrics", ApiRoutes.Metrics),
    ("/config", ApiRoutes.Config),
    ("/config/logging", ApiRoutes.ConfigLogging),
    ("/adb", ApiRoutes.Adb)
  };

  public static IEndpointRouteBuilder MapLegacyGuardEndpoints(this IEndpointRouteBuilder app) {
    foreach (var (legacyRoot, canonicalRoot) in LegacyRoots) {
      MapLegacyGuard(app, legacyRoot, canonicalRoot);
    }

    return app;
  }

  private static void MapLegacyGuard(IEndpointRouteBuilder app, string legacyRoot, string canonicalRoot) {
    app.MapMethods(legacyRoot, new[] { HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete },
      (HttpContext ctx) => Results.Json(
        new { error = new { code = "legacy_route", message = "Use the canonical API base path.", hint = canonicalRoot } },
        statusCode: StatusCodes.Status410Gone))
      .ExcludeFromDescription();

    app.MapMethods($"{legacyRoot}/{{*rest}}", new[] { HttpMethods.Get, HttpMethods.Post, HttpMethods.Put, HttpMethods.Patch, HttpMethods.Delete },
      (HttpContext ctx) => Results.Json(
        new { error = new { code = "legacy_route", message = "Use the canonical API base path.", hint = canonicalRoot } },
        statusCode: StatusCodes.Status410Gone))
      .ExcludeFromDescription();
  }
}
