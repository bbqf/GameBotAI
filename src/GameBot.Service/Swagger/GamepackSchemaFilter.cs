using System.Collections.Generic;
using GameBot.Service.Endpoints;
using GameBot.Service.Endpoints.Dto;
using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 143 (FR-017): describes the gamepack schemas and the read-only <c>owner</c> field on the sequence and command
/// reads. The service does not give XML comments to Swagger, so the descriptions come from this filter.
/// </summary>
internal sealed class GamepackSchemaFilter : ISchemaFilter {
  internal const string OwnerDescription =
    "The read-only owner of the object (feature 143). kind is user when no gamepack owns the object, or gamepack with the "
    + "name and version of the gamepack. A write request ignores this field. The owner shows only for an object that exists.";

  internal const string FileDescription =
    "A gamepack file: a manifest and the lists sequences, commands, and images. A sequence and a command use the JSON of the "
    + "stored object (member names are not case-sensitive). An image has id, contentType (image/png or image/jpeg), filename, "
    + "and dataBase64 (at most 10,000,000 bytes decoded). The request can have at most 100 MB. The manifest has name (1 to 64 "
    + "characters from letters, digits, underscore, hyphen, and dot; no leading dot; no reserved device name), version, "
    + "changelog, and the optional requiredApiMajor. The install refuses a gamepack when requiredApiMajor differs from the running API major version "
    + "(read it with GET /api/version). The field is optional. "
    + "The optional list seedTemplates (feature 150) holds queue templates in the shape of the queue settings document: name and entries, "
    + "and each entry has sequence (a sequence name), enabled, scheduleType, timerTimeOfDay or timerRelativeOffset, and parameterValues. "
    + "Only a first install of the gamepack creates a seed, and only when no template has the same name. Later installs, updates, and rollbacks ignore the seeds.";

  private static readonly Dictionary<string, string> ProblemFields = new() {
    ["category"] = "structure, reference, conflict, or signature.",
    ["objectType"] = "manifest, sequence, command, or image.",
    ["name"] = "The name of the object, or null.",
    ["owner"] = "For a conflict: user, or the gamepack name and version as text (for example PackName 1.2.0). Else null.",
    ["message"] = "What is wrong, in plain words.",
    ["code"] = "For a signature problem: signature_ and the status. Else not set."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(GamepacksEndpoints.GamepackInstallRequestBody)) {
      schema.Description = FileDescription;
      return;
    }

    if (context.Type == typeof(GamepackOwnerDto)) {
      schema.Description = OwnerDescription;
      if (schema.Properties.TryGetValue("kind", out var kind)) {
        kind.Description = "user or gamepack.";
        kind.Enum = new List<IOpenApiAny> { new OpenApiString("user"), new OpenApiString("gamepack") };
      }

      return;
    }

    if (context.Type == typeof(GamepackProblemDto)) {
      schema.Description = "One problem that the check found. The check collects all problems.";
      foreach (var (name, description) in ProblemFields) {
        if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
      }

      return;
    }

    if (context.Type == typeof(GamepackInstallResponse)) {
      schema.Description = "The result of an install or a dry run. A dry run with problems gives 200 with valid false. "
        + "An install with problems gives 422. created, updated, and removed show what the install did, or would do for a dry run. "
        + "takenOver is the number of user objects that the gamepack took over (feature 149). It is always present and is 0 when takeOver is off.";
      Describe(schema, "takenOver", "The number of user objects of the same type and name that the install took over (planned for a dry run). 0 when takeOver is off or nothing was taken over. It does not count an update of an object that the gamepack already owned.");
      Describe(schema, "takeOverBackupId", "The id of the take-over backup. Present only for a real install that replaced at least one object. Use it with POST /api/gamepacks/takeover-backups/{backupId}/restore.");
      Describe(schema, "takeOvers", "Present only for a dry run with takeOver=true and no problem. One entry for each object to take over, with identical true when the content of the user object equals the content of the gamepack object.");
      Describe(schema, "seedTemplatesCreated", "The names of the seed templates that the install created (planned for a dry run). Always present, also when empty (feature 150). A seed is an ordinary queue template with no link to the gamepack.");
      Describe(schema, "seedTemplatesSkipped", "The seed templates that the install did not create (planned for a dry run). Always present, also when empty. Each item has the name and the reason skipped_exists (a template with this name exists).");
      return;
    }

    if (context.Type == typeof(GamepackSeedSkipDto)) {
      schema.Description = "One seed template that an install did not create.";
      Describe(schema, "name", "The template name from the gamepack file.");
      Describe(schema, "reason", "skipped_exists: a queue template with this name exists. This is the only reason now.");
      return;
    }

    if (context.Type == typeof(GamepackTakeOverEntryDto)) {
      schema.Description = "One object that an install takes over from the user.";
      Describe(schema, "type", "sequence, command, or image.");
      Describe(schema, "name", "The object name. For an image, the id.");
      Describe(schema, "identical", "True when the canonical content of the user object equals the canonical content of the gamepack object. The file name of an image does not count.");
      return;
    }

    if (context.Type == typeof(GamepackTakeOverRestoreResponse)) {
      schema.Description = "The result of a restore of a take-over backup that worked. The objects have their old content and the owner user. The backup file stays.";
      Describe(schema, "restored", "The number of objects that were put back.");
      return;
    }

    if (context.Type == typeof(GamepackTakeOverRestoreRefusedResponse)) {
      schema.Description = "A restore that a check refused (HTTP 409). Nothing changed. A second restore of the same backup gives owner_changed.";
      Describe(schema, "reasons", "One reason for each refused object.");
      return;
    }

    if (context.Type == typeof(GamepackTakeOverRestoreReasonDto)) {
      schema.Description = "One reason for a refused restore.";
      Describe(schema, "code", "object_missing (the object no longer exists), object_changed (the live object differs from the content that the install wrote), or owner_changed (the gamepack no longer owns the object, or is not installed). There are no other codes.");
      Describe(schema, "type", "sequence, command, or image.");
      Describe(schema, "name", "The object name. For an image, the id.");
      return;
    }

    if (context.Type == typeof(GamepackRollbackResponse)) {
      schema.Description = "The result of a rollback that worked. After this, no previous version is kept.";
      return;
    }

    if (context.Type == typeof(GamepackRollbackRefusedResponse)) {
      schema.Description = "A rollback that the conflict and reference check refused. Nothing changed.";
      return;
    }

    if (context.Type == typeof(GamepackListItemDto)) {
      schema.Description = "One installed gamepack.";
      return;
    }

    if (ApplySignatureSchemas(schema, context) || ApplyCatalogSchemas(schema, context)) {
      return;
    }

    if ((context.Type == typeof(CommandResponse) || context.Type == typeof(SequenceListItemResponse))
        && schema.Properties.TryGetValue("owner", out var owner)) {
      owner.Description = OwnerDescription;
    }
  }

  // Feature 146: the gamepack signature types. The status names are snake_case.
  private static bool ApplySignatureSchemas(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(GamepackSignatureStatusDto)) {
      schema.Description = "The result of the signature check on an install or a dry run.";
      Describe(schema, "status", "valid, missing, invalid, content_mismatch, untrusted, or not_checked (the policy is off).");
      Describe(schema, "policy", "The policy at check time: required, optional, or off.");
      Describe(schema, "wouldReject", "True only when the policy is required and the status is not valid.");
      return true;
    }

    if (context.Type == typeof(GamepackSignaturePolicyDto)) {
      schema.Description = "The signature policy: required, optional, or off. The default is off.";
      Describe(schema, "policy", "required, optional, or off.");
      return true;
    }

    if (context.Type == typeof(GamepackSignatureKeyDto)) {
      schema.Description = "A trusted signing key. The public key text is not shown.";
      Describe(schema, "label", "The display label. It has no effect on trust.");
      Describe(schema, "fingerprint", "The SHA-256 of the key bytes, in lower case hex.");
      return true;
    }

    if (context.Type == typeof(GamepackSignatureKeyRequest)) {
      schema.Description = "A request to trust a signing key.";
      Describe(schema, "label", "The display label, 1 to 100 characters.");
      Describe(schema, "publicKey", "The ECDSA P-256 public key as base64 SubjectPublicKeyInfo.");
      return true;
    }

    return false;
  }

  // Feature 147: the remote catalog types.
  private static bool ApplyCatalogSchemas(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(GamepackCatalogSettingsDto)) {
      schema.Description = "The catalog settings. The token is never shown. The token and allowInsecure apply to web addresses only. A folder catalog ignores both.";
      Describe(schema, "location", "A web address or a full folder path. Null when no catalog is set.");
      Describe(schema, "allowInsecure", "True when http is allowed for a host that is not a loopback host. Web addresses only.");
      Describe(schema, "tokenSet", "True when a token is saved.");
      return true;
    }

    if (context.Type == typeof(GamepackCatalogSettingsRequest)) {
      schema.Description = "A request to change the catalog settings. The token and allowInsecure apply to web addresses only. A folder catalog ignores both.";
      Describe(schema, "location", "A web address (https, or http for a loopback host or with allowInsecure) or a full folder path. Null or empty clears it.");
      Describe(schema, "allowInsecure", "True to allow http for a host that is not a loopback host. Web addresses only.");
      Describe(schema, "token", "The bearer token. Null keeps the saved token. An empty text clears it. Other text sets it. Web addresses only.");
      return true;
    }

    if (context.Type == typeof(GamepackCatalogListResponse)) {
      schema.Description = "The gamepacks of the catalog, read from the index on each request.";
      return true;
    }

    if (context.Type == typeof(GamepackCatalogItemDto)) {
      schema.Description = "One gamepack of the catalog with its versions from new to old.";
      Describe(schema, "installedVersion", "The installed version, or null.");
      return true;
    }

    if (context.Type == typeof(GamepackCatalogVersionDto)) {
      schema.Description = "One version of a catalog gamepack.";
      Describe(schema, "changelog", "The change text. Empty when the index has none.");
      Describe(schema, "installed", "True for the installed version.");
      Describe(schema, "updateAvailable", "True when no version is installed, or this version is newer than the installed one.");
      return true;
    }

    if (context.Type == typeof(GamepackCatalogInstallRequest)) {
      schema.Description = "A request to install one listed version from the catalog.";
      Describe(schema, "name", "The gamepack name.");
      Describe(schema, "version", "The version.");
      Describe(schema, "dryRun", "True to check only. No data changes.");
      Describe(schema, "takeOver", "True to take over user objects of the same type and name (default false). The response has takenOver and takeOverBackupId.");
      return true;
    }

    return false;
  }

  private static void Describe(OpenApiSchema schema, string name, string description) {
    if (schema.Properties.TryGetValue(name, out var property)) {
      property.Description = description;
    }
  }
}
