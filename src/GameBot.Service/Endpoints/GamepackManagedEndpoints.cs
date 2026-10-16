using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Catalog;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.Service.Endpoints.Dto;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The managed gamepack update routes (feature 148): update from a file and update from the catalog. The managed rollback is
/// the <c>managed=true</c> query of the rollback route. The routes use the same API token rule as all other routes. The
/// handlers are named methods, because the taint analyzers cost more on large lambdas.
/// </summary>
internal static class GamepackManagedEndpoints {
  public static IEndpointRouteBuilder MapGamepackManagedEndpoints(this IEndpointRouteBuilder app) {
    var gamepacks = app.MapGroup(ApiRoutes.Gamepacks).WithTags("Gamepacks");

    gamepacks.MapPost("update", UpdateAsync)
      .Accepts<GamepacksEndpoints.GamepackInstallRequestBody>("application/json", "multipart/form-data")
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status200OK)
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status400BadRequest)
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status422UnprocessableEntity)
      .Produces<ManagedErrorDto>(StatusCodes.Status409Conflict)
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status500InternalServerError)
      .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
      .WithName("UpdateGamepack")
      .WithSummary("Update a gamepack from a file in one managed call")
      .WithDescription(
        "Finds the affected queues, stops them, stores a backup, installs the file, books disabled entries for new sequences, and "
        + "restarts the queues that the call stopped. Body: the gamepack file as application/json, or multipart/form-data with the part "
        + "gamepack and the optional part signature (at most 100 MB). Query dryRun=true returns the plan and all problems and changes nothing. "
        + "Query templates (repeatable) limits the call to the named templates.");

    var catalog = app.MapGroup(ApiRoutes.GamepackCatalog).WithTags("Gamepacks");
    catalog.MapPost("update", CatalogUpdateAsync)
      .Accepts<GamepackCatalogUpdateRequest>("application/json")
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status200OK)
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status422UnprocessableEntity)
      .Produces<ManagedErrorDto>(StatusCodes.Status400BadRequest)
      .Produces<ManagedErrorDto>(StatusCodes.Status404NotFound)
      .Produces<ManagedErrorDto>(StatusCodes.Status409Conflict)
      .Produces<ManagedErrorDto>(StatusCodes.Status502BadGateway)
      .Produces<ManagedErrorDto>(StatusCodes.Status504GatewayTimeout)
      .Produces<ManagedUpdateReportDto>(StatusCodes.Status500InternalServerError)
      .WithName("UpdateGamepackFromCatalog")
      .WithSummary("Update a gamepack from the catalog in one managed call")
      .WithDescription(
        "Gets the listed file from the catalog and does the same managed flow as the file route. The catalog failures have the same "
        + "status codes as feature 147. A failure of the catalog leaves the instance as it was.");

    return app;
  }

  private static async Task<IResult> UpdateAsync(
      HttpRequest http,
      GamepackUpdateOrchestrator orchestrator,
      GamepackSignatureChecker checker,
      bool? dryRun,
      [FromQuery] string[]? templates,
      CancellationToken ct) {
    var query = ManagedUpdateQuery.From(dryRun, templates);
    GamepacksEndpoints.InstallRequestParts parts;
    try {
      parts = await GamepacksEndpoints.ReadRequestAsync(http, checker.CurrentPolicy() != SignaturePolicy.Off, ct).ConfigureAwait(false);
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) {
      return TooLarge();
    }
    catch (InvalidDataException) {
      return ManagedUpdateHttp.Error(StatusCodes.Status400BadRequest, ManagedUpdateReasons.InvalidRequest, "The multipart request is not valid.");
    }

    if (parts.TooLarge) {
      return TooLarge();
    }

    if (parts.Gamepack is null) {
      return ManagedUpdateHttp.Error(StatusCodes.Status400BadRequest, ManagedUpdateReasons.InvalidRequest, "The multipart request has no gamepack part.");
    }

    var report = await orchestrator.RunAsync(query.ToUpdateRequest(parts.Gamepack, parts.Signature), ct).ConfigureAwait(false);
    return ManagedUpdateHttp.ToResult(report);
  }

  private static async Task<IResult> CatalogUpdateAsync(
      GamepackCatalogUpdateRequest? request,
      GamepackUpdateOrchestrator orchestrator,
      GamepackCatalogService catalog,
      CancellationToken ct) {
    if (string.IsNullOrWhiteSpace(request?.Name) || string.IsNullOrWhiteSpace(request.Version)) {
      return ManagedUpdateHttp.Error(StatusCodes.Status400BadRequest, ManagedUpdateReasons.InvalidRequest, "The request needs a name and a version.");
    }

    CatalogFetchResult fetched;
    try {
      fetched = await catalog.FetchAsync(request.Name, request.Version, ct).ConfigureAwait(false);
    }
    catch (CatalogException ex) {
      return CatalogFailed(ex);
    }

    var query = ManagedUpdateQuery.From(request.DryRun, request.Templates);
    var report = await orchestrator.RunAsync(query.ToUpdateRequest(fetched.FileText, fetched.SignatureText), ct).ConfigureAwait(false);
    return ManagedUpdateHttp.ToResult(report);
  }

  private static IResult CatalogFailed(CatalogException ex) {
    var (status, reason) = ex.Code switch {
      GameBot.Domain.Gamepacks.Catalog.CatalogFailure.NotConfigured => (StatusCodes.Status409Conflict, ManagedUpdateReasons.NotConfigured),
      GameBot.Domain.Gamepacks.Catalog.CatalogFailure.EntryNotListed => (StatusCodes.Status404NotFound, ManagedUpdateReasons.EntryNotListed),
      GameBot.Domain.Gamepacks.Catalog.CatalogFailure.InvalidLocation => (StatusCodes.Status400BadRequest, ManagedUpdateReasons.CatalogError),
      GameBot.Domain.Gamepacks.Catalog.CatalogFailure.Timeout => (StatusCodes.Status504GatewayTimeout, ManagedUpdateReasons.CatalogError),
      _ => (StatusCodes.Status502BadGateway, ManagedUpdateReasons.CatalogError)
    };
    return ManagedUpdateHttp.Error(status, reason, ex.Message);
  }

  private static IResult TooLarge() =>
    ManagedUpdateHttp.Error(StatusCodes.Status413PayloadTooLarge, ManagedUpdateReasons.TooLarge, "The request is larger than 100 MB. Nothing changed.");
}
