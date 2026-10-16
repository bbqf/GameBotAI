using System.Text;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.Service.Endpoints.Dto;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

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
      .Accepts<GamepackInstallRequestBody>("application/json", "multipart/form-data")
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
        + "The request can have at most 100 MB. The request is the gamepack file as application/json (no signature), or multipart/form-data "
        + "with the part gamepack and the optional part signature. The response has a signature block with status, policy, and wouldReject. "
        + "If the policy is required and the status is not valid, an install gives 422 with result signature_rejected and changes nothing. "
        + "A dry run gives 200 and shows wouldReject. If the policy is off, the signature part is not read and the status is not_checked.");

    group.MapGet("", List)
      .Produces<IReadOnlyList<GamepackListItemDto>>(StatusCodes.Status200OK)
      .WithName("ListGamepacks")
      .WithSummary("List the installed gamepacks")
      .WithDescription("Gives the name, version, and change text of each installed gamepack.");

    app.MapGamepackSignatureEndpoints();

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

  private static async Task<IResult> InstallAsync(
      HttpRequest http, GamepackService service, GamepackSignatureChecker checker, bool? dryRun, CancellationToken ct) {
    var isDryRun = dryRun ?? false;
    InstallRequestParts parts;
    try {
      parts = await ReadRequestAsync(http, checker.CurrentPolicy() != SignaturePolicy.Off, ct).ConfigureAwait(false);
    }
    catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge) {
      return TooLarge();
    }
    catch (InvalidDataException) {
      return ProblemResult(StatusCodes.Status400BadRequest, "bad_request", "The multipart request is not valid.");
    }

    if (parts.TooLarge) {
      return TooLarge();
    }

    if (parts.Gamepack is null) {
      return ProblemResult(StatusCodes.Status400BadRequest, "missing_gamepack_part", "The multipart request has no gamepack part.");
    }

    var outcome = await service.InstallAsync(parts.Gamepack, isDryRun, parts.Signature, ct).ConfigureAwait(false);
    return outcome.Status switch {
      GamepackInstallStatus.Ok => Results.Ok(ToInstallResponse(outcome, valid: true)),
      GamepackInstallStatus.InvalidJson => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status400BadRequest),
      GamepackInstallStatus.SignatureRejected when isDryRun => Results.Ok(ToInstallResponse(outcome, valid: false)),
      GamepackInstallStatus.SignatureRejected => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status422UnprocessableEntity),
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
    Removed = outcome.Removed,
    Result = outcome.Status == GamepackInstallStatus.SignatureRejected ? "signature_rejected" : null,
    Signature = outcome.Signature is null ? null : GamepackSignatureStatusDto.From(outcome.Signature)
  };

  private sealed record InstallRequestParts(string? Gamepack, string? Signature, bool TooLarge);

  // Reads the gamepack and, when the policy is not off, the signature part. Under the policy off, the signature part is
  // skipped with no read (feature 146, FR-009).
  private static async Task<InstallRequestParts> ReadRequestAsync(HttpRequest http, bool readSignature, CancellationToken ct) {
    if (http.ContentLength > MaxRequestBytes) {
      return new InstallRequestParts(null, null, TooLarge: true);
    }

    var limit = http.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>();
    if (limit is { IsReadOnly: false }) {
      limit.MaxRequestBodySize = MaxRequestBytes;
    }

    if (!IsMultipart(http, out var boundary)) {
      var body = await ReadCappedAsync(http.Body, ct).ConfigureAwait(false);
      return body is null ? new InstallRequestParts(null, null, TooLarge: true) : new InstallRequestParts(body, null, TooLarge: false);
    }

    var reader = new MultipartReader(boundary, http.Body) { BodyLengthLimit = MaxRequestBytes };
    string? gamepack = null;
    string? signature = null;
    MultipartSection? section;
    while ((section = await reader.ReadNextSectionAsync(ct).ConfigureAwait(false)) is not null) {
      var name = SectionName(section);
      if (string.Equals(name, "gamepack", StringComparison.OrdinalIgnoreCase) && gamepack is null) {
        gamepack = await ReadCappedAsync(section.Body, ct).ConfigureAwait(false);
        if (gamepack is null) {
          return new InstallRequestParts(null, null, TooLarge: true);
        }
      }
      else if (readSignature && string.Equals(name, "signature", StringComparison.OrdinalIgnoreCase) && signature is null) {
        signature = await ReadCappedAsync(section.Body, ct).ConfigureAwait(false);
        if (signature is null) {
          return new InstallRequestParts(null, null, TooLarge: true);
        }
      }
    }

    return new InstallRequestParts(gamepack, signature, TooLarge: false);
  }

  private static string? SectionName(MultipartSection section) =>
    ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out var disposition)
      ? HeaderUtilities.RemoveQuotes(disposition.Name).Value
      : null;

  private static bool IsMultipart(HttpRequest http, out string boundary) {
    boundary = string.Empty;
    if (!MediaTypeHeaderValue.TryParse(http.ContentType, out var media)
        || media.MediaType.Value is not { } type
        || !type.StartsWith("multipart/", StringComparison.OrdinalIgnoreCase)) {
      return false;
    }

    var value = HeaderUtilities.RemoveQuotes(media.Boundary).Value;
    if (string.IsNullOrEmpty(value)) {
      throw new InvalidDataException("The multipart boundary is missing.");
    }

    boundary = value;
    return true;
  }

  // Reads a stream with a size cap. Returns null when the stream is larger than the limit.
  private static async Task<string?> ReadCappedAsync(Stream body, CancellationToken ct) {
    using var buffer = new MemoryStream();
    var chunk = new byte[81920];
    int read;
    while ((read = await body.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0) {
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
