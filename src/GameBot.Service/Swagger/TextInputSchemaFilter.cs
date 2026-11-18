using GameBot.Domain.Commands;
using GameBot.Service.Models;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameBot.Service.Swagger;

/// <summary>
/// Feature 192: describes the text of a TextInput step, the request field dryRun of the command
/// write routes, and the typed length of a step result. The service does not feed XML comments to
/// Swagger, so this filter sets the text.
/// </summary>
internal sealed class TextInputSchemaFilter : ISchemaFilter {
  internal static readonly string TextDescription =
    "The text to type into the input field that has the focus on the device. The service types the full text as text, "
    + "not as a key name or a key code: \"250\" types the three characters 2, 5, and 0. Permitted characters: only "
    + "the printable ASCII characters from U+0020 (space) to U+007E (~). Length: 1 to "
    + TextInputRules.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture)
    + " characters. The text can hold inline placeholders, for example {{fighterBatch}}. The fieldTemplates key "
    + "textInput.text (one whole placeholder) replaces the inline text at run time; with that key, the text can be "
    + "empty. The save checks a literal text. The run checks the resolved text. Reason codes of the step result: "
    + TextInputRules.EmptyTextReason + " (the text is empty), "
    + TextInputRules.TooLongReason + " (more than " + TextInputRules.MaxLength.ToString(System.Globalization.CultureInfo.InvariantCulture)
    + " characters), " + TextInputRules.UnsupportedCharacterReason + " (a character that is not permitted), "
    + TextInputRules.NotAcceptedReason + " (the device did not accept the input), and "
    + TextInputRules.MissingConfigReason + " (the step has no textInput config).";

  internal const string TypedLengthDescription =
    "The number of characters that a TextInput step typed. For executed, the text length. For not_accepted, the "
    + "characters that the device accepted before the failure (0 when the check of the text failed). Not written "
    + "for other step types. The step result does not hold the typed text.";

  internal const string DryRunDescription =
    "Optional. When true, the service runs all save checks and stores nothing. A valid body gives 200 "
    + "{ valid: true, dryRun: true, errors: [] }. An invalid body gives the same error as a save.";

  public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
    if (context.Type == typeof(TextInputConfigDto)) {
      if (schema.Properties is not null && schema.Properties.TryGetValue("text", out var text)) {
        text.Description = TextDescription;
        text.Example = new OpenApiString("250");
      }
    }
    else if (context.Type == typeof(StepExecutionOutcomeDto)) {
      Describe(schema, "typedLength", TypedLengthDescription);
    }
    else if (context.Type == typeof(CreateCommandRequest) || context.Type == typeof(UpdateCommandRequest)
        || context.Type == typeof(CommandCreateSchema)) {
      Describe(schema, "dryRun", DryRunDescription);
    }
  }

  private static void Describe(OpenApiSchema schema, string property, string description) {
    if (schema.Properties is not null && schema.Properties.TryGetValue(property, out var propertySchema)) {
      propertySchema.Description = description;
    }
  }
}
