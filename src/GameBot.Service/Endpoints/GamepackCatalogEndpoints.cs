using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Service.Endpoints.Dto;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The routes for the remote gamepack catalog (feature 147): settings, list, and install. The routes use the same API token rule
/// as all other routes. No route installs without a request, and no route starts a poll. The handlers are named methods,
/// because the taint analyzers cost more on large lambdas.
/// </summary>
internal static class GamepackCatalogEndpoints {
  public static IEndpointRouteBuilder MapGamepackCatalogEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.GamepackCatalog).WithTags("Gamepacks");

    group.MapGet("settings", GetSettings)
      .Produces<GamepackCatalogSettingsDto>(StatusCodes.Status200OK)
      .WithName("GetGamepackCatalogSettings")
      .WithSummary("Read the catalog settings")
      .WithDescription("Gives the location, the allowInsecure flag, and tokenSet. The token is never in the response.");

    group.MapPut("settings", PutSettings)
      .Accepts<GamepackCatalogSettingsRequest>("application/json")
      .Produces<GamepackCatalogSettingsDto>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .WithName("SetGamepackCatalogSettings")
      .WithSummary("Set the catalog settings")
      .WithDescription(
        "Sets the catalog location (a web address or a full folder path), allowInsecure, and the token. Token: null keeps the saved "
        + "token, an empty text clears it, other text sets it. The token and allowInsecure apply to web addresses only. "
        + "A folder catalog ignores both. The next request uses the new settings. A restart is not needed.");

    group.MapGet("", ListAsync)
      .Produces<GamepackCatalogListResponse>(StatusCodes.Status200OK)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status409Conflict)
      .ProducesProblem(StatusCodes.Status502BadGateway)
      .ProducesProblem(StatusCodes.Status504GatewayTimeout)
      .WithName("ListGamepackCatalog")
      .WithSummary("List the gamepacks of the catalog")
      .WithDescription(
        "Reads the catalog index on each request. Gives each gamepack with its versions from new to old, the change text, "
        + "the installed marker, and updateAvailable. The instance never changes.");

    group.MapPost("install", InstallAsync)
      .Accepts<GamepackCatalogInstallRequest>("application/json")
      .Produces<GamepackInstallResponse>(StatusCodes.Status200OK)
      .Produces<GamepackInstallResponse>(StatusCodes.Status422UnprocessableEntity)
      .ProducesProblem(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status409Conflict)
      .ProducesProblem(StatusCodes.Status502BadGateway)
      .ProducesProblem(StatusCodes.Status504GatewayTimeout)
      .WithName("InstallGamepackFromCatalog")
      .WithSummary("Install one listed gamepack version from the catalog, or check it with dryRun")
      .WithDescription(
        "Downloads the listed file (and the optional signature file), checks the SHA-256 when the entry has one, and uses the same "
        + "install flow and the same signature policy as the manual install route. A name or version that the index does not list "
        + "gives 404 entry_not_listed before any download. A failure leaves the instance as it was.");

    return app;
  }

  private static IResult GetSettings(IGamepackCatalogSettingsStore store) => Results.Ok(ToDto(store.Load()));

  private static IResult PutSettings(GamepackCatalogSettingsRequest? request, IGamepackCatalogSettingsStore store) {
    try {
      return Results.Ok(ToDto(store.Save(request?.Location, request?.AllowInsecure ?? false, request?.Token)));
    }
    catch (CatalogException ex) {
      return Failure(ex);
    }
  }

  private static async Task<IResult> ListAsync(GamepackCatalogService service, CancellationToken ct) {
    try {
      var items = await service.ListAsync(ct).ConfigureAwait(false);
      return Results.Ok(new GamepackCatalogListResponse {
        Gamepacks = items.Select(i => new GamepackCatalogItemDto {
          Name = i.Name,
          InstalledVersion = i.InstalledVersion,
          Versions = i.Versions.Select(v => new GamepackCatalogVersionDto {
            Version = v.Version,
            Changelog = v.Changelog,
            Installed = v.Installed,
            UpdateAvailable = v.UpdateAvailable
          }).ToList()
        }).ToList()
      });
    }
    catch (CatalogException ex) {
      return Failure(ex);
    }
  }

  private static async Task<IResult> InstallAsync(GamepackCatalogInstallRequest? request, GamepackCatalogService service, CancellationToken ct) {
    if (string.IsNullOrWhiteSpace(request?.Name) || string.IsNullOrWhiteSpace(request.Version)) {
      return Problem(StatusCodes.Status400BadRequest, "invalid_request", "The request needs a name and a version.");
    }

    try {
      var outcome = await service.InstallAsync(request.Name, request.Version, request.DryRun, ct).ConfigureAwait(false);
      return GamepacksEndpoints.ToInstallResult(outcome, request.DryRun);
    }
    catch (CatalogException ex) {
      return Failure(ex);
    }
  }

  private static GamepackCatalogSettingsDto ToDto(GamepackCatalogSettings settings) => new() {
    Location = settings.Location,
    AllowInsecure = settings.AllowInsecure,
    TokenSet = !string.IsNullOrEmpty(settings.Token)
  };

  private static IResult Failure(CatalogException ex) => Problem(StatusFor(ex.Code), ex.Code, ex.Message);

  private static int StatusFor(string code) => code switch {
    CatalogFailure.NotConfigured => StatusCodes.Status409Conflict,
    CatalogFailure.InvalidLocation => StatusCodes.Status400BadRequest,
    CatalogFailure.EntryNotListed => StatusCodes.Status404NotFound,
    CatalogFailure.Timeout => StatusCodes.Status504GatewayTimeout,
    _ => StatusCodes.Status502BadGateway
  };

  private static IResult Problem(int status, string code, string message) =>
    Results.Problem(
      statusCode: status,
      title: message,
      extensions: new Dictionary<string, object?> { ["code"] = code, ["message"] = message });
}
