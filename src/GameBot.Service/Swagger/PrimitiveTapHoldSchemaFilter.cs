using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 111 (issue #235): describes the hold duration of a PrimitiveTap step and of its step outcome. The
/// service does not feed XML comments to Swagger. The range (minimum and maximum) comes from the Range
/// attribute on the DTO.
/// </summary>
internal sealed class PrimitiveTapHoldSchemaFilter : ISchemaFilter {
  internal const string ConfigHoldDescription =
    "Optional. Hold duration in milliseconds, 0 to 5000. More than 0: press and hold at the detected point "
    + "(plus offsets) for this duration. Absent or 0: a single tap.";

  internal const string OutcomeHoldDescription =
    "Hold duration in milliseconds of a PrimitiveTap step that pressed and held. Absent for a single tap and for "
    + "other steps.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(PrimitiveTapConfigDto)) {
      Describe(schema, "holdMs", ConfigHoldDescription);
    }
    else if (context.Type == typeof(StepExecutionOutcomeDto)) {
      Describe(schema, "holdMs", OutcomeHoldDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
