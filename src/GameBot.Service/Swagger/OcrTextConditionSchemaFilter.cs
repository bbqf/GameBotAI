using System.Collections.Generic;
using GameBot.Service.Models;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Issue #338 (FR-012): describes the <c>ocrText</c> step condition. The service does not give XML comments to
/// Swagger, so the field descriptions and the rules come from this filter.
/// </summary>
internal sealed class OcrTextConditionSchemaFilter : ISchemaFilter {
  internal const string SchemaDescription =
    "A step condition with the discriminator type: \"ocrText\" (issue #338). It is permitted in every slot "
    + "that accepts a step condition, and as a child of all, any and none. "
    + "True when the text that OCR reads from the region matches expected or pattern. Set exactly one of "
    + "expected and pattern. White space at the start and end of the text is removed before the compare. "
    + "A failure to read (no session, capture fault, OCR engine fault, region outside the frame, parse failure) "
    + "makes the condition false and the run does not fail. The run log shows the text that was read and one "
    + "reason code: no-session, capture-failed, ocr-unavailable, region-outside-frame, region-invalid, "
    + "ocr-error, or parse-failed. A pattern that times out is a plain mismatch. The condition sends no input "
    + "to the emulator. The web UI does not know this type: write ocrText conditions through the API.";

  private static readonly Dictionary<string, string> FieldDescriptions = new() {
    ["region"] = "The screen region to read, in capture pixels (x, y, width, height). Width and height are more "
      + "than zero. The region must be fully inside the frame at run time.",
    ["expected"] = "Exact, case-sensitive match against the trimmed text. Set exactly one of expected and pattern.",
    ["pattern"] = "A regular expression that matches anywhere in the trimmed text. A match has a time limit of "
      + "1 second. Set exactly one of expected and pattern.",
    ["parser"] = "Optional. A parser name: hh:mm:ss. The name is not case-sensitive, and the service stores the "
      + "lower-case name. The compare then uses the parsed text. Only the sequence endpoint checks the name.",
    ["negate"] = "When true, the result is inverted."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(OcrTextConditionContract) || schema.Properties is null) return;

    schema.Description = SchemaDescription;
    foreach (var (name, description) in FieldDescriptions) {
      if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
    }
  }
}
