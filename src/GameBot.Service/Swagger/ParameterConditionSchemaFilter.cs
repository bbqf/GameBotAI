using System.Collections.Generic;
using System.Linq;
using GameBot.Domain.Commands;
using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 193: describes the <c>parameter</c> step condition. The service does not give XML comments to
/// Swagger, so the field descriptions, the operator enum, the value kinds, and the rules come from this filter.
/// </summary>
internal sealed class ParameterConditionSchemaFilter : ISchemaFilter {
  internal const string SchemaDescription =
    "A step condition with the discriminator type: \"parameter\" (feature 193). It is permitted in every "
    + "slot that accepts a step condition (condition, if.condition, loop.condition, breakCondition), and as a "
    + "child of all, any and none. It compares the run value of a declared sequence parameter with a fixed value. "
    + "Compare rules: a parameter of the type number compares as a whole number (also a choice parameter of the "
    + "type number, and a number parameter with a pattern: the compare uses the entered number, before the pattern). "
    + "A parameter of the type text (input kinds text, choice and image) compares as text, ordinal and "
    + "case-insensitive, with no trim. A choice parameter compares the option value, not the label. "
    + "greaterThan and lessThan on text use the same ordinal, case-insensitive order. negate is applied after the compare. "
    + "Save rules (HTTP 400 on create, update, patch, and their dryRun): name must be a declared parameter of the "
    + "sequence (no {{ }}, no queue built-in), operator must be one of equals, notEquals, greaterThan, lessThan, "
    + "value is required and must be a JSON string or a JSON number, value must be a whole number for a number "
    + "parameter, and a multi-value parameter is refused. A read returns value with the JSON kind that the author wrote. "
    + "Run failures: no run value (no binding and no default) fails the step with ParameterUnresolved; a run value "
    + "that is not a whole number for a number parameter fails the step with ParameterValueInvalid. "
    + "The run log shows one line for each evaluation, for example "
    + "parameter(name=fighterBatch, operator=equals, value=0) actual=0 compare=number. "
    + "Example: { \"type\": \"parameter\", \"name\": \"fighterBatch\", \"operator\": \"equals\", \"value\": 0, \"negate\": true }.";

  private static readonly Dictionary<string, string> FieldDescriptions = new() {
    ["name"] = "The bare name of a declared parameter of the sequence. Case-sensitive. No {{ }}.",
    ["operator"] = "One of equals, notEquals, greaterThan, lessThan. Case-sensitive.",
    ["value"] = "The fixed value: a JSON string or a JSON number. For a number parameter, a whole number (or a text that is a whole number).",
    ["negate"] = "When true, the result is inverted."
  };

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type != typeof(ParameterConditionContract) || schema.Properties is null) return;

    schema.Description = SchemaDescription;
    foreach (var (name, description) in FieldDescriptions) {
      if (schema.Properties.TryGetValue(name, out var property)) property.Description = description;
    }

    if (schema.Properties.TryGetValue("operator", out var operatorSchema)) {
      operatorSchema.Enum = ParameterConditionRules.Operators
        .Select(op => (IOpenApiAny)new OpenApiString(op))
        .ToList();
    }

    if (schema.Properties.ContainsKey("value")) {
      schema.Properties["value"] = new OpenApiSchema {
        Description = FieldDescriptions["value"],
        Nullable = true,
        OneOf = new List<OpenApiSchema> {
          new() { Type = "string" },
          new() { Type = "integer" }
        }
      };
    }
  }
}
