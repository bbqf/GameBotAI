using System.Collections.Generic;
using GameBot.Domain.Commands;
using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 105 (FR-013): describes the <c>lastRun</c> step condition. The service does not give XML
/// comments to Swagger, so the field descriptions, the <c>status</c> values, the <c>since</c> and
/// <c>within</c> patterns and the two rules come from this filter.
/// </summary>
internal sealed class LastRunConditionSchemaFilter : ISchemaFilter {
  internal const string ExactlyOneWindowRule =
    "Set exactly one of since or within. The window ends now; both ends are included.";

  internal const string NoQueueRule =
    "The condition reads the run statistics of the current queue. In a run that has no queue (an ad-hoc run "
    + "from POST /api/sequences/{sequenceId}/execute, or a dry-run) the condition is false, and the run does "
    + "not fail.";

  internal const string SchemaDescription =
    "A step condition with the discriminator type: \"lastRun\" (feature 105). It is permitted in every slot "
    + "that accepts a step condition, and as a child of all, any and none. "
    + "True when the named sequence has a completed run with the given status in the current queue, and the "
    + "end time of that run is in the window. Only the 100 most recent runs of each (queue, sequence) pair "
    + "are kept. " + ExactlyOneWindowRule + " " + NoQueueRule + " negate and the none composite invert the "
    + "result. The web UI does not know this type: write lastRun conditions through the API.";

  private static readonly Dictionary<string, string> FieldDescriptions = new() {
    ["sequence"] = "'self' (the sequence that contains the step), or a sequence ID. The save path does not "
      + "check that the ID exists; an unknown ID gives false.",
    ["status"] = "The status of the run to look for: success, failure or cancelled (not case-sensitive).",
    ["since"] = "A service-local time of day in HH:mm format (00:00 to 23:59). The window starts at the most "
      + "recent occurrence of that time, at or before now. On a daylight-saving day, it is the most recent "
      + "real occurrence. Set exactly one of since or within.",
    ["within"] = "A duration in hh:mm:ss or d.hh:mm:ss format, more than zero and not more than 366 days. The "
      + "hours part can be 24 or more: 24:00:00 is 24 hours. The window starts at now minus the duration. "
      + "Set exactly one of since or within.",
    ["negate"] = "When true, the result is inverted."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(LastRunConditionContract) || schema.Properties is null) return;

    schema.Description = SchemaDescription;
    foreach (var (name, description) in FieldDescriptions) {
      if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
    }

    if (schema.Properties.TryGetValue("status", out var status)) {
      status.Enum = new List<IOpenApiAny> {
        new OpenApiString("success"),
        new OpenApiString("failure"),
        new OpenApiString("cancelled")
      };
    }

    if (schema.Properties.TryGetValue("since", out var since)) since.Pattern = LastRunConditionRules.SincePattern;
    if (schema.Properties.TryGetValue("within", out var within)) within.Pattern = LastRunConditionRules.WithinPattern;
  }
}
