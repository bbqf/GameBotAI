using System.Collections.Generic;
using GameBot.Service.Contracts.Queues;
using GameBot.Service.Services.QueueExecution;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 096 (FR-009, issue #199): describes the pause fields on a queue's <c>health</c> block. The
/// service does not feed XML comments to Swagger, and it was the unpublished meaning of <c>paused</c> that
/// let an idle-paused queue be read as not paused.
/// </summary>
internal sealed class QueueHealthSchemaFilter : ISchemaFilter {
  private const string PausedDescription =
    "Whether the run is paused right now, for either reason: an idle pause (routine; the game is backed out "
    + "between firings and the pause ends by itself when the next firing is due) or a failure-policy pause "
    + "(the run is parked after too many consecutive failed cycles and is released only by "
    + "POST /api/queues/{id}/resume). pauseKind tells them apart.";

  private const string PausedAtDescription =
    "When the reported pause began (service-local clock). Null when not paused.";

  private const string PauseReasonDescription =
    "Why the run is paused: 'idle pause: resumes at HH:mm' for an idle pause, or a text starting "
    + "'failure policy:' for a failure-policy pause. Null when not paused.";

  private const string PauseKindDescription =
    $"Which pause is in force: '{QueuePauseKinds.Idle}' or '{QueuePauseKinds.FailurePolicy}'; null when not "
    + $"paused. If both were in force at once, '{QueuePauseKinds.FailurePolicy}' is reported, because it is the "
    + "one that needs an operator's resume.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(QueueHealthResponse) || schema.Properties is null) return;

    Describe(schema, "paused", PausedDescription);
    Describe(schema, "pausedAt", PausedAtDescription);
    Describe(schema, "pauseReason", PauseReasonDescription);
    if (schema.Properties.TryGetValue("pauseKind", out var pauseKind)) {
      pauseKind.Description = PauseKindDescription;
      pauseKind.Enum = new List<IOpenApiAny> {
        new OpenApiString(QueuePauseKinds.Idle),
        new OpenApiString(QueuePauseKinds.FailurePolicy)
      };
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
