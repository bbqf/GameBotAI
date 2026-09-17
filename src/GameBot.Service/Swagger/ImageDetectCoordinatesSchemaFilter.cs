using System.Collections.Generic;
using GameBot.Service.Endpoints.Dto;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 101 (issue #188): states the coordinate unit of every match field on the two detection routes.
/// POST /api/images/detect reports x/y/width/height (and bbox) as fractions of the capture frame, while
/// POST /api/images/detect-all reports the same box in pixels, under the same field names. The difference is
/// documented rather than changed so existing callers keep working. The service does not feed XML comments to
/// Swagger.
/// </summary>
internal sealed class ImageDetectCoordinatesSchemaFilter : ISchemaFilter {
  internal const string DetectXDescription =
    "Left edge of the match, from the frame's left edge, as a fraction of the capture frame width"
    + ", clamped to 0..1 — not pixels. Multiply by the capture's width for pixels. POST /api/images/detect-all "
    + "reports the same box in pixels under the same field name.";

  internal const string DetectYDescription =
    "Top edge of the match, from the frame's top edge, as a fraction of the capture frame height"
    + ", clamped to 0..1 — not pixels. Multiply by the capture's height for pixels. POST /api/images/detect-all "
    + "reports the same box in pixels under the same field name.";

  internal const string DetectWidthDescription =
    "Width of the match as a fraction of the capture frame width"
    + ", clamped to 0..1 — not pixels. Multiply by the capture's width for pixels. POST /api/images/detect-all "
    + "reports the same box in pixels under the same field name.";

  internal const string DetectHeightDescription =
    "Height of the match as a fraction of the capture frame height"
    + ", clamped to 0..1 — not pixels. Multiply by the capture's height for pixels. POST /api/images/detect-all "
    + "reports the same box in pixels under the same field name.";

  internal const string BboxDescription =
    "The same box as this match's top-level x/y/width/height, repeated with identical values: fractions of the "
    + "capture frame, not pixels.";

  internal const string TemplateIdDescription =
    "Id of the requested reference image. POST /api/images/detect-all reports the same identifier as imageId.";

  internal const string DetectAllXDescription =
    "Left edge of the match in pixels of the capture frame, from the frame's left edge — not fractions. "
    + "POST /api/images/detect reports the same box as fractions of the frame.";

  internal const string DetectAllYDescription =
    "Top edge of the match in pixels of the capture frame, from the frame's top edge — not fractions. "
    + "POST /api/images/detect reports the same box as fractions of the frame.";

  internal const string DetectAllWidthDescription =
    "Width of the match in pixels of the capture frame — not fractions. "
    + "POST /api/images/detect reports the same box as fractions of the frame.";

  internal const string DetectAllHeightDescription =
    "Height of the match in pixels of the capture frame — not fractions. "
    + "POST /api/images/detect reports the same box as fractions of the frame.";

  internal const string ImageIdDescription =
    "Id of the reference image that matched. POST /api/images/detect reports the same identifier as templateId.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null) return;

    if (context.Type == typeof(MatchResult)) {
      DescribeDetectCoordinates(schema);
      Describe(schema, "templateId", TemplateIdDescription);
      DescribeReference(schema, "bbox", BboxDescription);
    }
    else if (context.Type == typeof(NormalizedRect)) {
      DescribeDetectCoordinates(schema);
    }
    else if (context.Type == typeof(DetectAllMatch)) {
      Describe(schema, "x", DetectAllXDescription);
      Describe(schema, "y", DetectAllYDescription);
      Describe(schema, "width", DetectAllWidthDescription);
      Describe(schema, "height", DetectAllHeightDescription);
      Describe(schema, "imageId", ImageIdDescription);
    }
  }

  private static void DescribeDetectCoordinates(OpenApiSchema schema) {
    Describe(schema, "x", DetectXDescription);
    Describe(schema, "y", DetectYDescription);
    Describe(schema, "width", DetectWidthDescription);
    Describe(schema, "height", DetectHeightDescription);
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }

  /// <summary>
  /// OpenAPI 3.0 ignores siblings of a bare <c>$ref</c>, so a described reference is wrapped in
  /// <c>allOf</c> — the same shape Swashbuckle emits for described references.
  /// </summary>
  private static void DescribeReference(OpenApiSchema schema, string property, string description) {
    if (!schema.Properties.TryGetValue(property, out var propertySchema)) return;
    if (propertySchema.Reference is null) {
      propertySchema.Description = description;
      return;
    }
    schema.Properties[property] = new OpenApiSchema {
      AllOf = new List<OpenApiSchema> { propertySchema },
      Description = description
    };
  }
}
