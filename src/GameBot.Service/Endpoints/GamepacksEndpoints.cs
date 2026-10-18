using System.Text;
using GameBot.Domain.Gamepacks;
using GameBot.Domain.Gamepacks.Managed;
using GameBot.Domain.Gamepacks.Signing;
using GameBot.Service.Endpoints.Dto;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;

namespace GameBot.Service.Endpoints;

/// <summary>
/// The gamepack API (feature 143): install (with dry run), list, rollback, and remove of the record. The remove route keeps the objects. The
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
        + "Query takeOver=true changes this for an object of the user only: the gamepack takes the object over, the id stays, and the old content goes to one "
        + "take-over backup (takeOverBackupId) that the route POST /api/gamepacks/takeover-backups/{backupId}/restore can put back. "
        + "A conflict with an object of another gamepack stays a conflict. With takeOver=true and dryRun=true, the response has the list takeOvers "
        + "(type, name, identical). The count takenOver is always in the response. "
        + "On a first install the file member seedTemplates creates queue templates whose name is free. The response always has the lists "
        + "seedTemplatesCreated and seedTemplatesSkipped (for a dry run they show what the install would do). "
        + "Status 400 also covers a condition or loop config with a missing, unknown, duplicate, or not-string type member. The message gives the JSON path. "
        + "The type member can be at any position in its object. "
        + "The request can have at most 100 MB. The request is the gamepack file as application/json (no signature), or multipart/form-data "
        + "with the part gamepack and the optional part signature. The response has a signature block with status, policy, and wouldReject. "
        + "If the policy is required and the status is not valid, an install gives 422 with result signature_rejected and changes nothing. "
        + "A dry run gives 200 and shows wouldReject. If the policy is off, the signature part is not read and the status is not_checked. "
        + "SIGNATURE FILE: The signature part is a UTF-8 JSON text, not raw signature bytes: "
        + "{\"format\":\"gamepack-signature-1\",\"algorithm\":\"ecdsa-p256-sha256\",\"publicKey\":\"<base64 SubjectPublicKeyInfo of a P-256 key>\","
        + "\"manifestSha256\":\"<64 lower case hex>\",\"files\":[{\"type\":\"sequence|command|image\",\"name\":\"<name, or id for an image>\","
        + "\"sha256\":\"<64 lower case hex>\"}],\"signature\":\"<base64 of 64 bytes>\"}. "
        + "SIGNED BYTES: The platform does not sign or check the raw gamepack file bytes. The signer signs the UTF-8 bytes of the canonical JSON of "
        + "{\"files\":[...],\"format\":\"gamepack-signature-1\",\"manifestSha256\":\"...\"}. Each item of files has the keys name, sha256, and type. "
        + "Sort files by type, then by lower case name (ordinal). The platform builds these bytes again from the signature file, "
        + "so the signed files list must be the sorted list. "
        + "HASHES: manifestSha256 is the SHA-256 (lower case hex) of the canonical JSON of the manifest object (of the text null if the file has no manifest). "
        + "For each item of sequences, commands (name field), and images (id field), sha256 is the SHA-256 of the canonical JSON of that item. "
        + "An unknown field is part of the hash. A name or id that is not a string gives an empty name. "
        + "CANONICAL JSON: no white space. Object keys in ordinal order at each level. Array order stays. Strings are UTF-8. "
        + "Only these are escaped: quote, backslash, \\b, \\f, \\n, \\r, \\t, and \\u00xx (lower case hex) for other characters below U+0020. "
        + "All other characters, also non-ASCII and slash, are raw. An integer is plain digits, with no fraction and no exponent. "
        + "Other numbers use the .NET round-trip text of a double with the invariant culture (the exponent is written as e, with no plus sign). "
        + "true, false, and null are lower case. "
        + "ALGORITHM: ECDSA, curve P-256, SHA-256. The signature is the IEEE P1363 form (r then s, 64 bytes), not DER, written as base64. "
        + "In .NET use ECDsa.SignData(bytes, HashAlgorithmName.SHA256). "
        + "STATUS: invalid = the text is empty, is not JSON, has an unknown format or algorithm, a bad key, a bad base64 text, a signature that is not 64 bytes, "
        + "or the signature does not pass for the signed bytes and the key in the file. A signature over the raw file bytes gives invalid. "
        + "content_mismatch = the signature is correct, but the hashes differ from the gamepack (changed, extra, or missing item). "
        + "untrusted = the signature and the content are correct, but the SHA-256 fingerprint of the key (lower case hex of the SPKI bytes) is not on the trusted list. "
        + "valid = all checks pass. missing = no signature part. "
        + "ACTION STEP: An Action step of a sequence can run a command. Write the step as in POST /api/sequences: primitiveAction with payload, "
        + "commandReference, or the step member commandId. Each form keeps the command action. The stored form (action with parameters, as in an export) also works. "
        + "The same rules apply to a step in a Loop body, and in the body or elseBody of a conditional step. "
        + "Only an Action step takes these three members. A step member that the install cannot keep, and two members that name different commands, "
        + "give a structure problem with the sequence name, the step ID, and the field name. A command that is not in the file and not on the system gives a reference problem. "
        + "Sample: {\"manifest\":{\"name\":\"demo-pack\",\"version\":\"1.0.0\"},\"sequences\":[{\"name\":\"Run the demo command\",\"steps\":["
        + "{\"stepId\":\"s1\",\"stepType\":\"Action\",\"primitiveAction\":{\"type\":\"command\",\"payload\":{\"commandId\":\"cmd-1\"}},"
        + "\"commandReference\":{\"commandId\":\"cmd-1\",\"commandName\":\"Demo command\"}}]}],"
        + "\"commands\":[{\"id\":\"cmd-1\",\"name\":\"Demo command\",\"steps\":[{\"type\":\"KeyInput\",\"order\":0,\"keyInput\":{\"key\":\"BACK\"}}]}]}. "
        + "The catalog index (index.json) is described on GET /api/gamepacks/catalog. "
        + "WRITE ERRORS: A failed write gives 500 with result failed. The detail member has this text: "
        + "Cannot replace file '<full path>' for <kind> '<id>': <reason>. The kind is sequence, command, image, template, registry, or previous. "
        + "If the restore fails too, the result is partial_restore and the problem document has the extension member failedRestoreKeys: "
        + "a list of strings, one for each file in an unknown state, in the format <kind>:<id> (<full file path>).");

    group.MapPost("takeover-backups/{backupId}/restore", RestoreTakeOverAsync)
      .Produces<GamepackTakeOverRestoreResponse>(StatusCodes.Status200OK)
      .Produces<GamepackTakeOverRestoreRefusedResponse>(StatusCodes.Status409Conflict)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status500InternalServerError)
      .WithName("RestoreGamepackTakeOverBackup")
      .WithSummary("Give the objects of a take-over backup back to the user")
      .WithDescription(
        "Puts back the old content of the objects that one install with takeOver=true replaced. The objects are owned by the user again. "
        + "The route takes the same gate as install and rollback. It does not stop or start a queue. "
        + "It checks three things for each object: object_missing (the object no longer exists), object_changed (the live object differs from the "
        + "content that the install wrote), and owner_changed (the gamepack no longer owns the object, or the gamepack is not installed). "
        + "If a check fails, the route gives 409 with the list of reasons and changes nothing. A restore works as one unit: all objects or none. "
        + "A second restore of the same backup gives 409 with owner_changed and changes nothing, because the backup file stays. "
        + "An id that is unknown or has a wrong form gives 404. A failed write gives 500. "
        + "If the undo fails too, the result is partial_restore and the problem document has failedRestoreKeys "
        + "(the format is <kind>:<id> (<full file path>)).");

    group.MapGet("", List)
      .Produces<IReadOnlyList<GamepackListItemDto>>(StatusCodes.Status200OK)
      .WithName("ListGamepacks")
      .WithSummary("List the installed gamepacks")
      .WithDescription("Gives the name, version, and change text of each installed gamepack.");

    app.MapGamepackSignatureEndpoints();
    app.MapGamepackCatalogEndpoints();
    app.MapGamepackManagedEndpoints();

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
        + "If the check finds a problem, the rollback is refused and nothing changes. "
        + "Query managed=true runs the managed flow of feature 148: it stops the affected queues, stores a backup, rolls back, "
        + "books disabled entries for sequences that the previous version adds, and restarts the queues that it stopped. "
        + "The response is then a managed update report. The queries dryRun and templates apply only with managed=true. "
        + "A failed write gives 500. If the restore fails too, the result is partial_restore and the problem document has failedRestoreKeys "
        + "(the format is <kind>:<id> (<full file path>)).");

    group.MapDelete("{name}", RemoveAsync)
      .Produces(StatusCodes.Status204NoContent)
      .ProducesProblem(StatusCodes.Status404NotFound)
      .ProducesProblem(StatusCodes.Status500InternalServerError)
      .WithName("RemoveGamepack")
      .WithSummary("Remove the record of an installed gamepack")
      .WithDescription(
        "Removes the registry record of the gamepack and all its owner entries. The sequences, commands, and images of the gamepack stay and "
        + "belong to the user. The kept previous version is deleted. Take-over backup files stay. The route takes the same gate as install and rollback. "
        + "It gives 204 with no body when the record is removed. A name that is unknown or not valid gives 404 with result not_found and changes nothing. "
        + "A failed registry write gives 500 with result failed, and the registry stays as before. "
        + "A later install of the same name is a first install.");

    return app;
  }

  /// <summary>The request body of the install route: a gamepack file. Used for the OpenAPI document only.</summary>
  internal sealed class GamepackInstallRequestBody { }

  private static async Task<IResult> InstallAsync(
      HttpRequest http, GamepackService service, GamepackSignatureChecker checker, bool? dryRun, bool? takeOver, CancellationToken ct) {
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

    var outcome = await service.InstallAsync(parts.Gamepack, new GamepackInstallOptions(isDryRun, takeOver ?? false, parts.Signature), ct).ConfigureAwait(false);
    return ToInstallResult(outcome, isDryRun);
  }

  private static async Task<IResult> RestoreTakeOverAsync(string backupId, GamepackService service, CancellationToken ct) {
    // The public route never uses the force mode. The service method takes the install gate and checks all three reasons.
    var outcome = await service.RestoreTakeOverAsync(backupId, ct).ConfigureAwait(false);
    return outcome.Status switch {
      TakeOverRestoreStatus.Restored => Results.Ok(new GamepackTakeOverRestoreResponse { BackupId = outcome.BackupId, Restored = outcome.Restored }),
      TakeOverRestoreStatus.NotFound => ProblemResult(StatusCodes.Status404NotFound, "not_found", "No take-over backup has this id."),
      TakeOverRestoreStatus.Refused => Results.Json(
        new GamepackTakeOverRestoreRefusedResponse {
          BackupId = outcome.BackupId,
          Reasons = outcome.Reasons.Select(r => new GamepackTakeOverRestoreReasonDto { Code = r.Code, Type = r.Type, Name = r.Name, Message = r.Message }).ToList()
        },
        statusCode: StatusCodes.Status409Conflict),
      TakeOverRestoreStatus.PartialRestore => WriteFailed("partial_restore", outcome.ErrorMessage, outcome.FailedRestoreKeys),
      _ => WriteFailed("failed", outcome.ErrorMessage)
    };
  }

  /// <summary>Maps an install outcome to the response of the install routes (the manual route and the catalog route).</summary>
  internal static IResult ToInstallResult(GamepackInstallOutcome outcome, bool isDryRun) =>
    outcome.Status switch {
      GamepackInstallStatus.Ok => Results.Ok(ToInstallResponse(outcome, valid: true)),
      GamepackInstallStatus.InvalidJson => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status400BadRequest),
      GamepackInstallStatus.SignatureRejected when isDryRun => Results.Ok(ToInstallResponse(outcome, valid: false)),
      GamepackInstallStatus.SignatureRejected => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status422UnprocessableEntity),
      GamepackInstallStatus.Invalid when isDryRun => Results.Ok(ToInstallResponse(outcome, valid: false)),
      GamepackInstallStatus.Invalid => Results.Json(ToInstallResponse(outcome, valid: false), statusCode: StatusCodes.Status422UnprocessableEntity),
      GamepackInstallStatus.PartialRestore => WriteFailed("partial_restore", outcome.ErrorMessage, outcome.FailedRestoreKeys),
      _ => WriteFailed("failed", outcome.ErrorMessage)
    };

  private static IResult List(GamepackService service) =>
    Results.Ok(service.List().Select(g => new GamepackListItemDto {
      Name = g.Name,
      Version = g.Version,
      Changelog = g.Changelog,
      RequiredApiMajor = g.RequiredApiMajor,
      HasPreviousVersion = g.HasPreviousVersion,
      InstalledAtUtc = g.InstalledAtUtc
    }).ToList());

  private static async Task<IResult> RollbackAsync(
      string name, GamepackService service, IServiceProvider services, bool? managed, bool? dryRun, [FromQuery] string[]? templates, CancellationToken ct) {
    if (managed == true) {
      // Feature 148: the managed rollback uses the same flow as the update. Without managed=true, nothing changes.
      // The orchestrator is resolved only here, so a plain rollback does not build the queue engine graph.
      var orchestrator = services.GetRequiredService<GamepackUpdateOrchestrator>();
      var query = ManagedUpdateQuery.From(dryRun, templates, managed);
      var report = await orchestrator.RunAsync(query.ToRollbackRequest(name), ct).ConfigureAwait(false);
      return ManagedUpdateHttp.ToResult(report);
    }

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
      GamepackRollbackStatus.PartialRestore => WriteFailed("partial_restore", outcome.ErrorMessage, outcome.FailedRestoreKeys),
      _ => WriteFailed("failed", outcome.ErrorMessage)
    };
  }

  private static async Task<IResult> RemoveAsync(string name, GamepackService service, CancellationToken ct) {
    var outcome = await service.RemoveAsync(name, ct).ConfigureAwait(false);
    return outcome.Status switch {
      GamepackRemoveStatus.Removed => Results.NoContent(),
      GamepackRemoveStatus.NotFound => ProblemResult(StatusCodes.Status404NotFound, "not_found", "No gamepack has this name."),
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
    TakenOver = outcome.TakenOver,
    TakeOverBackupId = outcome.TakeOverBackupId,
    TakeOvers = outcome.TakeOvers?.Select(GamepackTakeOverEntryDto.From).ToList(),
    SeedTemplatesCreated = outcome.SeedTemplatesCreated,
    SeedTemplatesSkipped = outcome.SeedTemplatesSkipped.Select(GamepackSeedSkipDto.From).ToList(),
    Result = outcome.Status == GamepackInstallStatus.SignatureRejected ? "signature_rejected" : null,
    Signature = outcome.Signature is null ? null : GamepackSignatureStatusDto.From(outcome.Signature)
  };

  internal sealed record InstallRequestParts(string? Gamepack, string? Signature, bool TooLarge);

  // Reads the gamepack and, when the policy is not off, the signature part. Under the policy off, the signature part is
  // skipped with no read (feature 146, FR-009). The managed update route (feature 148) uses it too.
  internal static async Task<InstallRequestParts> ReadRequestAsync(HttpRequest http, bool readSignature, CancellationToken ct) {
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

  private static IResult WriteFailed(string result, string? detail, IReadOnlyList<string>? failedRestoreKeys = null) =>
    ProblemResult(
      StatusCodes.Status500InternalServerError,
      result,
      result == "partial_restore"
        ? "A write failed and the restore failed too. The state can be partly restored. See the service log."
        : "A write failed. All objects stay as before.",
      detail,
      result == "partial_restore" ? failedRestoreKeys ?? Array.Empty<string>() : null);

  private static IResult ProblemResult(int status, string result, string title, string? detail = null, IReadOnlyList<string>? failedRestoreKeys = null) {
    var extensions = new Dictionary<string, object?> { ["result"] = result };
    if (failedRestoreKeys is not null) {
      extensions["failedRestoreKeys"] = failedRestoreKeys;
    }

    return Results.Problem(statusCode: status, title: title, detail: detail, extensions: extensions);
  }
}
