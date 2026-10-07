using System.Collections.Generic;
using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 128: adds a description to each property of the <c>POST /api/ocr/read</c> request and response.
/// The service does not feed XML comments to Swagger, so the descriptions are set here.
/// </summary>
internal sealed class OcrReadSchemaFilter : ISchemaFilter {
  private static readonly Dictionary<System.Type, Dictionary<string, string>> Descriptions = new() {
    [typeof(OcrReadRequest)] = new() {
      ["serial"] = "Serial of a running device session. Reads the live screen. Set exactly one of serial and captureId.",
      ["captureId"] = "Id of a stored capture. Reads the stored picture and takes no new capture. Set exactly one of serial and captureId.",
      ["region"] = "The region to read, in pixels of the frame. Required. It must be fully inside the frame.",
      ["parser"] = "Optional parser name. Supported name: hh:mm:ss (case does not matter)."
    },
    [typeof(OcrRegionDto)] = new() {
      ["x"] = "Left edge of the region, in pixels from the left edge of the frame. Zero or more.",
      ["y"] = "Top edge of the region, in pixels from the top edge of the frame. Zero or more.",
      ["width"] = "Width of the region in pixels. More than zero.",
      ["height"] = "Height of the region in pixels. More than zero."
    },
    [typeof(OcrReadResponse)] = new() {
      ["text"] = "Raw text from the OCR engine. Empty when the region has no text.",
      ["confidence"] = "Confidence value from the OCR engine. It has the same meaning as in the ocrOffset step.",
      ["source"] = "The source that was read: serial or captureId.",
      ["frameWidth"] = "Width of the frame in pixels.",
      ["frameHeight"] = "Height of the frame in pixels.",
      ["parser"] = "The parser name that ran. Null when the request has no parser.",
      ["parsed"] = "The parsed value. Set when a parser is named and the text parses.",
      ["parseFailureReason"] = "The reason the text did not parse. Set when a parser is named and the text does not parse. This is not an error."
    },
    [typeof(OcrParsedValue)] = new() {
      ["value"] = "The duration as hh:mm:ss text. Hours can be more than 24.",
      ["totalSeconds"] = "The duration in seconds."
    },
    [typeof(OcrErrorResponse)] = new() {
      ["code"] = "Error code, for example invalid_request, unknown_parser, invalid_region, serial_not_found, capture_not_found, capture_failed, capture_unavailable or ocr_unavailable.",
      ["message"] = "A message that says what to do."
    }
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (schema.Properties is null || !Descriptions.TryGetValue(context.Type, out var map)) {
      return;
    }
    foreach (var (name, description) in map) {
      if (!schema.Properties.TryGetValue(name, out var property)) {
        continue;
      }
      if (property.Reference is null) {
        property.Description = description;
      }
      else {
        // OpenAPI 3.0 ignores siblings of a bare $ref, so a described reference is wrapped in allOf.
        schema.Properties[name] = new OpenApiSchema {
          AllOf = new List<OpenApiSchema> { property },
          Description = description
        };
      }
    }
  }
}
