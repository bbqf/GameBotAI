using GameBot.Service.Contracts.QueueTemplates;
using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 114 (issue #243): tells how a parameter selects the reference image. It describes the
/// <c>fieldTemplates</c> of a command step, the <c>imageId</c> of an <c>imageVisible</c> condition, and
/// the <c>parameterValues</c> of a template entry. The service does not feed XML comments to Swagger.
/// </summary>
internal sealed class ParametrizedReferenceImageSchemaFilter : ISchemaFilter {
  internal const string FieldTemplatesDescription =
    "Optional. Parameter placeholders for fields of this step, keyed by dotted field path. "
    + "Numeric keys: swipe.startX, swipe.startY, swipe.endX, swipe.endY, swipe.durationMs, waitForImage.timeoutMs, "
    + "ensureEmulatorRunning.instanceIndex, ensureGameRunning.readinessTimeoutMs, "
    + "primitiveTap.detectionTarget.confidence, primitiveTap.detectionTarget.offsetX, "
    + "primitiveTap.detectionTarget.offsetY, ensureGameRunning.readinessImage.confidence, "
    + "ensureGameRunning.readinessImage.offsetX, ensureGameRunning.readinessImage.offsetY. "
    + "Image keys: primitiveTap.detectionTarget.referenceImageId, waitForImage.detectionTarget.referenceImageId. "
    + "The value of an image key must be one whole placeholder, for example {{name}}. At run time the resolved "
    + "value replaces the inline referenceImageId. The save gives the warning static_check_skipped for an image key. "
    + "Errors: an unknown key gives unknown_field_template_path. An image key with a value that is not one whole "
    + "placeholder gives invalid_field_template_value.";

  internal const string ImageIdDescription =
    "Id of the reference image. It can hold a parameter placeholder, for example {{name}} or nova-{{option}}, "
    + "in each condition position: condition, if.condition, loop.condition, breakCondition, and each child of "
    + "all, any and none. For a placeholder, the save does not check that the image exists and gives the warning "
    + "static_check_skipped. The run resolves the id against the scope of the step before the evaluation.";

  internal const string ParameterValuesDescription =
    "Optional. Parameter values of this entry. When a value, or a declared default, goes to an image field of the "
    + "sequence or of a command that the sequence can reach, the save checks that an image has that id. "
    + "A missing image gives 400 unknown_image_reference, and the service saves nothing.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(CommandStepDto)) {
      Describe(schema, "fieldTemplates", FieldTemplatesDescription);
    }
    else if (context.Type == typeof(ImageVisibleConditionContract)) {
      Describe(schema, "imageId", ImageIdDescription);
    }
    else if (context.Type == typeof(TemplateEntrySaveRequest)) {
      Describe(schema, "parameterValues", ParameterValuesDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
