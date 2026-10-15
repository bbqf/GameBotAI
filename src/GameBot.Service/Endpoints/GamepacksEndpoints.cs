using System.Text;
using GameBot.Domain.Gamepacks;
using GameBot.Service.Endpoints.Dto;
using Microsoft.AspNetCore.Http.Features;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The gamepack API (feature 143): install (with dry run), list, and rollback. No route uninstalls a gamepack. The
/// handlers are named methods, because the taint analyzers cost more on large lambdas.
/// </summary>
internal static class GamepacksEndpoints {
  /// <summary>The largest install request in bytes (100 MB).</summary>
  internal const long MaxRequestBytes = 100L * 1024 * 1024;

  public static IEndpointRouteBuilder MapGamepackEndpoints(this IEndpointRouteBuilder app) {
    var group = app.MapGroup(ApiRoutes.Gamepacks).WithTags("Gamepacks");

    group.MapPost("install", InstallAsync)
      .Accepts<GamepackInstallRequestBody>("application/json")
      .Produces<GamepackInstallResponse>(StatusCodes.Status200OK)
      .Produces<GamepackInstallResponse>(StatusCodes.Status422UnprocessableEntity)
      .Produces<GamepackInstallResponse>(StatusCodes.Status400BadRequest)
      .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
      .ProducesProblem(StatusCodes.Status500InternalServerError)
      .WithName("InstallGamepack")
      .WithSummary("Install a gamepack, or check it with dryRun")
      .WithDescription(
        "Installs all objects of a gamepack file as one unit, or none. Query dryRun=true checks the file and changes no data. "
        + "An object with the same type and name as an object of the user or of another gamepack is a conflict. "
        + "The request can have at most 100 MB.");

    group.MapGet("", List)
      .Produces<IReadOnlyList<GamepackListItemDto>>(StatusCodes.Status200OK)
      .WithName("ListGamepacks")
      .WithSummary("List the installed gamepacks")
      .WithDescription("Gives the name, version, and change text of each installed gamepack.");

    group.MapPost("{name}/rollback", RollbackAsync)
      .Produces<GamepackRollbackResponse>(StatusCodes.Status200OK)
      .Produces<GamepackRollbackRefusedResponse>(StatusCodes.Status422UnprocessableEntity)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status409Conflict)
      .ProducesProblem(StatusCodes.Status500InternalServerError)
      .WithName("RollbackGamepack")
      .WithSummary("Roll a gamepack back to its previous version")
      .WithDescription(
        "Runs the same conflict and reference check as an install on the kept previous content. "
        + "If the check finds a problem, the rollback is refused and nothing changes.");

    return app;
  }

  /// <summary>The request body of the install route: a gamepack file. Used for the OpenAPI document only.</summary>
  internal sealed class GamepackInstallRequestBody { }

  private static async Task<IResult> InstallAsync(HttpRequest http, GamepackService service, bool? dryRun, CancellationToken ct) {
    var isDryRun = dryRun ?? false;
    string json;
    try {
      var body = await ReadBodyAsync(http, ct).ConfigureAwait(false);
      if (body is null) {
        return TooLarge();
      }

      json = body;
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) {
      return TooLarge();
    }

    var outcome = await service.InstallAsync(json, isDryRun, ct).ConfigureAwait(false);
    return outcome.Status switch {
      GamepackInstallStatus.Ok => Results.Ok(ToInstallResponse(outcome, valid: true)),
      GamepackInstallStatus.InvalidJson => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status400BadRequest),
      GamepackInstallStatus.Invalid when isDryRun => Results.Ok(ToInstallResponse(outcome, valid: false)),
      GamepackInstallStatus.Invalid => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status422UnprocessableEntity),
      GamepackInstallStatus.PartialRestore => WriteFailed("partial_restore", outcome.ErrorMessage),
      _ => WriteFailed("failed", outcome.ErrorMessage)
    };
  }

  private static IResult List(GamepackService service) =>
    Results.Ok(service.List().Select(g => new GamepackListItemDto {
      Name = g.Name,
      Version = g.Version,
      Changelog = g.Changelog,
      RequiredApiMajor = g.RequiredApiMajor,
      HasPreviousVersion = g.HasPreviousVersion,
      InstalledAtUtc = g.InstalledAtUtc
    }).ToList());

  private static async Task<IResult> RollbackAsync(string name, GamepackService service, CancellationToken ct) {
    var outcome = await service.RollbackAsync(name, ct).ConfigureAwait(false);
    return outcome.Status switch {
      GamepackRollbackStatus.RolledBack => Results.Ok(new GamepackRollbackResponse {
        Name = outcome.Name,
        Version = outcome.Version,
        Created = outcome.Created,
        Updated = outcome.Updated,
        Removed = outcome.Removed
      }),
      GamepackRollbackStatus.Refused => Results.Json(
        new GamepackRollbackRefusedResponse { Problems = outcome.Problems.Select(GamepackProblemDto.From).ToList() },
        statusCode: StatusCodes.Status422UnprocessableEntity),
      GamepackRollbackStatus.NoPreviousVersion => ProblemResult(
        StatusCodes.Status409Conflict, "no_previous_version", "The gamepack has no kept previous version."),
      GamepackRollbackStatus.NotFound => ProblemResult(
        StatusCodes.Status404NotFound, "not_found", "No gamepack has this name."),
      GamepackRollbackStatus.PartialRestore => WriteFailed("partial_restore", outcome.ErrorMessage),
      _ => WriteFailed("failed", outcome.ErrorMessage)
    };
  }

  private static GamepackInstallResponse ToInstallResponse(GamepackInstallOutcome outcome, bool valid) => new() {
    DryRun = outcome.DryRun,
    Valid = valid,
    Problems = outcome.Problems.Select(GamepackProblemDto.From).ToList(),
    Gamepack = outcome.Name is null ? null : new GamepackRefDto(outcome.Name, outcome.Version),
    Created = outcome.Created,
    Updated = outcome.Updated,
    Removed = outcome.Removed
  };

  // Reads the body with a size cap. Returns null when the body is larger than the limit.
  private static async Task<string?> ReadBodyAsync(HttpRequest http, CancellationToken ct) {
    if (http.ContentLength > MaxRequestBytes) {
      return null;
    }

    var limit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) {
      limit.MaxRequestBodySize = MaxRequestBytes;
    }

    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = await http.Body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0) {
      if (buffer.Length + read > MaxRequestBytes) {
        return null;
      }

      await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
    }

    return Encoding.UTF8.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
  }

  private static IResult TooLarge() =>
    ProblemResult(StatusCodes.Status413PayloadTooLarge, "too_large", "The request is larger than 100 MB. Nothing changed.");

  private static IResult WriteFailed(string result, string? detail) =>
    ProblemResult(
      StatusCodes.Status500InternalServerError,
      result,
      result == "partial_restore"
        ? "A write failed and the restore failed too. The state can be partly restored. See the service log."
        : "A write failed. All objects stay as before.",
      detail);

  private static IResult ProblemResult(int status, string result, string title, string? detail = null) =>
    Results.Problem(
      statusCode: status,
      title: title,
      detail: detail,
      extensions: new Dictionary<string, object?> { ["result"] = result });
}
