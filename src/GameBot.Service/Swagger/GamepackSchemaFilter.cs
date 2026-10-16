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
    + "(read it with GET /api/version). The field is optional.";

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
        + "An install with problems gives 422. created, updated, and removed show what the install did, or would do for a dry run.";
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

    if (ApplySignatureSchemas(schema, context)) {
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

  private static void Describe(OpenApiSchema schema, string name, string description) {
    if (schema.Properties.TryGetValue(name, out var property)) {
      property.Description = description;
    }
  }
}
