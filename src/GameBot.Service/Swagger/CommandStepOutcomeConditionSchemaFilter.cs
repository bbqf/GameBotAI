using System.Collections.Generic;
using GameBot.Domain.Commands;
using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 167 (FR-011): describes the <c>commandStepOutcome</c> step condition. The service does not give XML
/// comments to Swagger, so the field descriptions and the rules come from this filter.
/// </summary>
internal sealed class CommandStepOutcomeConditionSchemaFilter : ISchemaFilter {
  internal const string SchemaDescription =
    "A step condition with the discriminator type: \"commandStepOutcome\" (feature 167). It is permitted in every "
    + "slot that accepts a step condition, and as a child of all, any and none. "
    + "True when one top-level step of the command that a prior sequence step ran ended in expectedState. "
    + "stepRef names the sequence step that runs the command. commandStepOrder is the order of the step in that "
    + "command. Only the steps of that command can be read, not the steps of a command that it calls. "
    + "A missing outcome makes the condition false (before negate) and the run does not fail. The outcome is "
    + "missing when the sequence step did not run its command, when the run is a dry run, when the step has "
    + "no readable state, or when the command has no step with that order. "
    + "A new run of the sequence step replaces the older outcome. "
    + "The save and the save dry run check that the command has a step with the order. The run does not check it. "
    + "The run log shows one line for each evaluation.";

  private static readonly Dictionary<string, string> FieldDescriptions = new() {
    ["stepRef"] = "The id of a prior sequence step that runs a command. The same reference rules as the "
      + "commandOutcome condition apply, and the step must run a command.",
    ["commandStepOrder"] = "The order of a top-level step in the command. The minimum is 1.",
    ["expectedState"] = "One of " + "executed, skipped_detection_failed, completed_timeout, image_detected"
      + " (not case-sensitive). executed: the step ran. skipped_detection_failed: the step did not run because "
      + "its image was not found. completed_timeout: the wait ended because of the time limit. "
      + "image_detected: the wait ended because the image appeared.",
    ["negate"] = "When true, the result is inverted."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(CommandStepOutcomeConditionContract) || schema.Properties is null) return;

    schema.Description = SchemaDescription;
    foreach (var (name, description) in FieldDescriptions) {
      if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
    }

    if (schema.Properties.TryGetValue("commandStepOrder", out var order)) {
      order.Minimum = 1;
    }

    if (schema.Properties.TryGetValue("expectedState", out var state)) {
      var values = new List<IOpenApiAny>();
      foreach (var allowed in CommandStepOutcomeStates.All) values.Add(new OpenApiString(allowed));
      state.Enum = values;
    }
  }
}
