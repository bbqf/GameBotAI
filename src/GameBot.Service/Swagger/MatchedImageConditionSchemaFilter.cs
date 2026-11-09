using System.Collections.Generic;
using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 181: describes the <c>matchedImage</c> step condition. The service does not give XML comments to
/// Swagger, so the field descriptions and the rules come from this filter.
/// </summary>
internal sealed class MatchedImageConditionSchemaFilter : ISchemaFilter {
  internal const string SchemaDescription =
    "A step condition with the discriminator type: \"matchedImage\" (feature 181). It is permitted in every "
    + "slot that accepts a step condition, and as a child of all, any and none. "
    + "True when the latest matched image of the step stepRef is imageId. "
    + "A step gives a matched image when it is a WaitForImage step that ended with image_detected, or when its own "
    + "condition is a top-level imageVisible condition (not negated) that was true. "
    + "A step with no matched image makes the condition false (before negate) and the run does not fail. "
    + "The run log shows one line for each evaluation. "
    + "A new run of the step replaces the older result. "
    + "The same reference rules as the commandOutcome condition apply to stepRef: it must name a prior step. "
    + "Example: { \"type\": \"matchedImage\", \"stepRef\": \"wait-quest\", \"imageId\": \"quest-a\" }.";

  private static readonly Dictionary<string, string> FieldDescriptions = new() {
    ["stepRef"] = "The id of a prior step that did an image check. The step must be prior in authored order.",
    ["imageId"] = "The image id to compare with the matched image id of that step. Not empty.",
    ["negate"] = "When true, the result is inverted."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(MatchedImageConditionContract) || schema.Properties is null) return;

    schema.Description = SchemaDescription;
    foreach (var (name, description) in FieldDescriptions) {
      if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
    }
  }
}
