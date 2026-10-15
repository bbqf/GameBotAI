using System.Collections.Generic;
using GameBot.Service.Endpoints.Dto;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>Feature 144 (FR-009): describes the API version response. The service gives no XML comments to Swagger.</summary>
internal sealed class ApiVersionSchemaFilter : ISchemaFilter {
  private static readonly Dictionary<string, string> Fields = new() {
    ["major"] = "The API major version. A gamepack requiredApiMajor must equal it.",
    ["minor"] = "The API minor version.",
    ["patch"] = "The API patch version.",
    ["schemaFormat"] = "The format version of the sequence and command schema."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(ApiVersionResponse)) {
      return;
    }

    schema.Description = "The API version of the platform. Two calls give the same values.";
    foreach (var (name, description) in Fields) {
      if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
    }
  }
}
