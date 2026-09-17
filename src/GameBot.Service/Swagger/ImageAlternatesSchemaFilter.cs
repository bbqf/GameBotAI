using GameBot.Service.Endpoints;
using GameBot.Service.Endpoints.Dto;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 097 (issue #192): describes the alternates schemas and the detect response's
/// <c>matchedReferenceId</c>. The service does not feed XML comments to Swagger.
/// </summary>
internal sealed class ImageAlternatesSchemaFilter : ISchemaFilter {
  internal const string MatchedReferenceIdDescription =
    "Id of the reference image that produced this match: the requested image itself, or one of its "
    + "alternates (PUT /api/images/{id}/alternates). templateId stays the requested image.";

  private const string AlternatesRequestDescription =
    "Alternate image ids in preference order (ties in score go to earlier entries). At most 8; each must be "
    + "a stored image, not the image itself, and listed once. An empty list clears the alternates.";

  private const string AlternatesResponseDescription =
    "Alternates in registered order. Every detection naming the image also scores these.";

  private const string ExistsDescription =
    "False when the alternate image has since been deleted; detection skips it and logs a warning.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null) return;

    if (context.Type == typeof(MatchResult)) {
      Describe(schema, "matchedReferenceId", MatchedReferenceIdDescription);
    }
    else if (context.Type == typeof(SetImageAlternatesRequest)) {
      Describe(schema, "alternates", AlternatesRequestDescription);
    }
    else if (context.Type == typeof(ImageAlternatesResponse)) {
      Describe(schema, "id", "The image whose alternates these are.");
      Describe(schema, "alternates", AlternatesResponseDescription);
    }
    else if (context.Type == typeof(ImageAlternateEntry)) {
      Describe(schema, "id", "The alternate image id.");
      Describe(schema, "exists", ExistsDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
